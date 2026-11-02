using System;
using System.Collections.Generic;
using System.Globalization;

namespace NotCounters.Core.Leaderboards
{
    public enum RetryAction
    {
        Success,
        Retry,
        GiveUp,
        CacheNotFound,
    }

    public readonly struct RetryDecision
    {
        public RetryDecision(RetryAction action, TimeSpan delay, string reason)
        {
            Action = action;
            Delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            _reason = reason;
        }

        private readonly string? _reason;

        public RetryAction Action { get; }

        public TimeSpan Delay { get; }

        public string Reason => _reason ?? string.Empty;

        public override string ToString() => Action + " (" + Delay.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s): " + Reason;
    }

    public sealed class RetryPolicy
    {
        public const string BeatLeaderResetHeader = "X-Rate-Limit-Reset";

        public const string ScoreSaberResetHeaderPrefix = "X-RateLimit-Reset-";

        public const string RetryAfterHeader = "Retry-After";

        public static RetryPolicy Default { get; } = new RetryPolicy();

        public int MaxTransientRetries { get; init; } = 1;

        public TimeSpan TransientRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

        public int MaxRateLimitRetries { get; init; } = 4;

        public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

        public TimeSpan MinimumRateLimitWait { get; init; } = TimeSpan.FromSeconds(1);

        public TimeSpan ScoreSaberDefaultRateLimitWait { get; init; } = TimeSpan.FromSeconds(60);

        public TimeSpan BeatLeaderDefaultRateLimitWait { get; init; } = TimeSpan.FromSeconds(10);

        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

        public RetryDecision Decide(LeaderboardService service, int attempt, int? httpStatus,
            IReadOnlyDictionary<string, string>? headers, DateTimeOffset now)
        {
            if (attempt < 1) attempt = 1;

            if (!httpStatus.HasValue)
            {
                return Transient(attempt, "network error or timeout");
            }

            int status = httpStatus.Value;
            if (status >= 200 && status < 300) return new RetryDecision(RetryAction.Success, TimeSpan.Zero, "HTTP " + status);
            if (status == 404) return new RetryDecision(RetryAction.CacheNotFound, TimeSpan.Zero, "HTTP 404: no leaderboard");
            if (status == 429) return RateLimited(service, attempt, headers, now);
            if (status >= 500 || status == 408) return Transient(attempt, "HTTP " + status);
            return new RetryDecision(RetryAction.GiveUp, TimeSpan.Zero, "HTTP " + status + " is not retried");
        }

        public TimeSpan RateLimitWait(LeaderboardService service, IReadOnlyDictionary<string, string>? headers, DateTimeOffset now)
        {
            TimeSpan? wait = service == LeaderboardService.BeatLeader
                ? ParseBeatLeaderReset(headers, now)
                : ParseScoreSaberReset(headers, now);
            wait ??= ParseRetryAfter(headers, now);
            return wait ?? (service == LeaderboardService.BeatLeader ? BeatLeaderDefaultRateLimitWait : ScoreSaberDefaultRateLimitWait);
        }

        public static TimeSpan? ParseBeatLeaderReset(IReadOnlyDictionary<string, string>? headers, DateTimeOffset now)
        {
            string? value = Header(headers, BeatLeaderResetHeader);
            if (string.IsNullOrWhiteSpace(value)) return null;
            string v = value!.Trim();
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return SecondsOrEpoch(v, now);
            }

            if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset reset))
            {
                return NonNegative(reset, now);
            }

            return null;
        }

        private static TimeSpan NonNegative(DateTimeOffset at, DateTimeOffset now) => at <= now ? TimeSpan.Zero : at - now;

        public static TimeSpan? ParseScoreSaberReset(IReadOnlyDictionary<string, string>? headers, DateTimeOffset now)
        {
            if (headers == null) return null;
            TimeSpan? best = null;
            foreach (KeyValuePair<string, string> kv in headers)
            {
                if (kv.Key == null || !kv.Key.Trim().StartsWith(ScoreSaberResetHeaderPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                TimeSpan? d = SecondsOrEpoch(kv.Value, now);
                if (d.HasValue && (!best.HasValue || d.Value > best.Value)) best = d;
            }

            return best;
        }

        public static TimeSpan? ParseRetryAfter(IReadOnlyDictionary<string, string>? headers, DateTimeOffset now)
        {
            string? value = Header(headers, RetryAfterHeader);
            if (string.IsNullOrWhiteSpace(value)) return null;
            string v = value!.Trim();
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            {
                return ToDelay(seconds);
            }

            if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset at))
            {
                return NonNegative(at, now);
            }

            return null;
        }

        private RetryDecision Transient(int attempt, string what)
        {
            if (attempt <= MaxTransientRetries)
            {
                return new RetryDecision(RetryAction.Retry, TransientRetryDelay, what + ": retrying once");
            }

            return new RetryDecision(RetryAction.GiveUp, TimeSpan.Zero, what + ": giving up for this song");
        }

        private RetryDecision RateLimited(LeaderboardService service, int attempt, IReadOnlyDictionary<string, string>? headers, DateTimeOffset now)
        {
            if (attempt > MaxRateLimitRetries)
            {
                return new RetryDecision(RetryAction.GiveUp, TimeSpan.Zero, "HTTP 429: too many rate-limit retries");
            }

            TimeSpan wait = RateLimitWait(service, headers, now);
            if (wait < MinimumRateLimitWait) wait = MinimumRateLimitWait;

            double factor = Math.Pow(2, Math.Min(attempt - 1, 30));
            double ticks = Math.Min((double)wait.Ticks * factor, MaxBackoff.Ticks);
            TimeSpan delay = ticks >= TimeSpan.MaxValue.Ticks ? TimeSpan.MaxValue : TimeSpan.FromTicks((long)Math.Max(0, ticks));
            return new RetryDecision(RetryAction.Retry, delay, "HTTP 429: rate limited");
        }

        private static TimeSpan? SecondsOrEpoch(string? raw, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!double.TryParse(raw!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
            if (v > 1e9 && v < 1e11)
            {
                double delta = v - now.ToUnixTimeMilliseconds() / 1000.0;
                return ToDelay(delta);
            }

            return ToDelay(v);
        }

        private static TimeSpan? ToDelay(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return null;
            if (seconds <= 0) return TimeSpan.Zero;
            if (seconds > TimeSpan.MaxValue.TotalSeconds / 2) return TimeSpan.FromDays(1);
            return TimeSpan.FromMilliseconds(seconds * 1000);
        }

        private static string? Header(IReadOnlyDictionary<string, string>? headers, string name)
        {
            if (headers == null) return null;
            if (headers.TryGetValue(name, out string? direct)) return direct;
            foreach (KeyValuePair<string, string> kv in headers)
            {
                if (kv.Key != null && string.Equals(kv.Key.Trim(), name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }

            return null;
        }
    }
}
