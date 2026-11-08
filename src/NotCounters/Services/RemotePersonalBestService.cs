using System;
using System.Collections.Generic;
using System.Globalization;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Leaderboards;
using NotCounters.Core.Stats;
using NotCounters.Util;
using SiraUtil.Logging;
using SiraUtil.Services;
using Zenject;

namespace NotCounters.Services
{
    internal sealed class RemotePersonalBestService : IRemotePersonalBestService, IInitializable, IDisposable
    {
        public const string NoPersonalBestText = PersonalBestStatus.NoPersonalBest;

        public const string DetectingAccountText = PersonalBestStatus.DetectingAccount;

        public const string NoAccountText = PersonalBestStatus.NoAccount;

        public const string AmbiguousAccountText = PersonalBestStatus.AmbiguousAccount;

        public const string DisabledText = PersonalBestStatus.Disabled;

        public const string UnavailableText = PersonalBestStatus.Unavailable;

        private readonly IConfigService _config;
        private readonly IPlayerAccountService _accounts;
        private readonly LeaderboardRequestScheduler _scheduler;
        private readonly ILeaderboardDataService _leaderboards;
        private readonly SiraLog _log;
        private readonly ILevelFinisher? _levelFinisher;

        private readonly LeaderboardCache<string, PersonalBestInfo> _cache =
            new LeaderboardCache<string, PersonalBestInfo>(LeaderboardCache<string, PersonalBestInfo>.SessionLifetime, comparer: StringComparer.Ordinal);

        private readonly Dictionary<string, PendingRequest> _inFlight = new Dictionary<string, PendingRequest>(StringComparer.Ordinal);

        private int _cacheGeneration;

        private int _clearCount;

        private double? _lastClearAt;
        private bool _disposed;

        public RemotePersonalBestService(IConfigService config, IPlayerAccountService accounts, LeaderboardRequestScheduler scheduler,
            ILeaderboardDataService leaderboards, SiraLog log, [InjectOptional] ILevelFinisher? levelFinisher)
        {
            _config = config;
            _accounts = accounts;
            _scheduler = scheduler;
            _leaderboards = leaderboards;
            _log = log;
            _levelFinisher = levelFinisher;
        }

        public IDisposable Request(LevelIdentity level, PbSource source, Action<PersonalBestInfo> onResult) =>
            Safe.Invoke("RemotePb.Request", () => RequestCore(level, source, onResult), ListenerSubscription.None);

        public void Initialize()
        {
            if (_levelFinisher != null)
            {
                _levelFinisher.StandardLevelFinished += OnStandardLevelFinished;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_levelFinisher != null)
            {
                _levelFinisher.StandardLevelFinished -= OnStandardLevelFinished;
            }

            _inFlight.Clear();
            _cache.Clear();
        }

        private void OnStandardLevelFinished(LevelCompletionResults results) =>
            Safe.Invoke("RemotePb.LevelFinished", () =>
            {
                _cacheGeneration++;
                _cache.Clear();

                if (results != null && results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared)
                {
                    _clearCount++;
                    _lastClearAt = MonotonicClock.Now;
                }
            });

        private IDisposable RequestCore(LevelIdentity? level, PbSource source, Action<PersonalBestInfo>? onResult)
        {
            if (onResult == null || _disposed)
            {
                return ListenerSubscription.None;
            }

            if (source == PbSource.Local)
            {
                Deliver(onResult, Unavailable(source, NoPersonalBestText));
                return ListenerSubscription.None;
            }

            if (level == null || !level.IsValid)
            {
                Deliver(onResult, Unavailable(source, NoPersonalBestText));
                return ListenerSubscription.None;
            }

            GlobalSettings settings = _config.Config.Global;
            bool beatLeader = source == PbSource.BeatLeader;
            if (!(beatLeader ? settings.EnableBeatLeader : settings.EnableScoreSaber))
            {
                Deliver(onResult, Unavailable(source, DisabledText));
                return ListenerSubscription.None;
            }

            PlayerAccount account = beatLeader ? _accounts.BeatLeader : _accounts.ScoreSaber;
            string playerId = account.Status == PlayerAccountStatus.Found ? (account.Id ?? string.Empty).Trim() : string.Empty;
            if (playerId.Length == 0)
            {
                Deliver(onResult, Unavailable(source, StatusText(account)));
                return ListenerSubscription.None;
            }

            string key = source + "|" + playerId + "|" + level;
            if (_cache.TryGet(key, out PersonalBestInfo? cached))
            {
                Deliver(onResult, cached);
                return ListenerSubscription.None;
            }

            if (_inFlight.TryGetValue(key, out PendingRequest? running) && running.ClearCount == _clearCount)
            {
                running.Listeners.Add(onResult);
                return new ListenerSubscription<PersonalBestInfo>(running.Listeners, onResult);
            }

            double now = MonotonicClock.Now;
            double wait = PersonalBestTiming.WaitSeconds(now, _lastClearAt);
            string? url = beatLeader
                ? new BeatLeaderApi(settings.BeatLeaderApiBaseUrl).PlayerScoreUrl(playerId, level)
                : new ScoreSaberApi(settings.ScoreSaberApiBaseUrl).PlayerScoreUrl(playerId, level,
                    PersonalBestTiming.CacheBuster(now, _lastClearAt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            if (url == null)
            {
                Deliver(onResult, Unavailable(source, NoPersonalBestText));
                return ListenerSubscription.None;
            }

            var pending = new PendingRequest(_cacheGeneration, _clearCount, onResult);
            _inFlight[key] = pending;
            if (wait > 0)
            {
                _log.Debug($"{source} personal best for {level} waits {wait.ToString("0.0", CultureInfo.InvariantCulture)} s for the leaderboard to receive the cleared play.");
            }

            LeaderboardService service = beatLeader ? LeaderboardService.BeatLeader : LeaderboardService.ScoreSaber;
            _scheduler.Enqueue(service, url, response => OnResponse(key, source, level, pending, response), wait);
            return new ListenerSubscription<PersonalBestInfo>(pending.Listeners, onResult);
        }

        private void OnResponse(string key, PbSource source, LevelIdentity level, PendingRequest pending, LeaderboardResponse response)
        {
            if (_disposed)
            {
                return;
            }

            if (_inFlight.TryGetValue(key, out PendingRequest? current) && ReferenceEquals(current, pending))
            {
                _inFlight.Remove(key);
            }

            PersonalBestInfo info = Safe.Invoke("RemotePb.Response",
                () => InterpretAndCache(key, source, level, pending.Generation, response),
                Unavailable(source, UnavailableText));

            Action<PersonalBestInfo>[] listeners = pending.Listeners.ToArray();
            pending.Listeners.Clear();
            foreach (Action<PersonalBestInfo> listener in listeners)
            {
                Deliver(listener, info);
            }
        }

        private PersonalBestInfo InterpretAndCache(string key, PbSource source, LevelIdentity level, int generation, LeaderboardResponse response)
        {
            PersonalBestInfo info;
            TimeSpan? cacheFor;
            switch (response.Outcome)
            {
                case LeaderboardRequestOutcome.Success:
                    PersonalBestParseStatus status = PersonalBestResponseParser.Parse(source, response.Http.Body,
                        MaxScoreHint(level, source), out info, out string? error);
                    if (status == PersonalBestParseStatus.NoScore)
                    {
                        info = Unavailable(source, NoPersonalBestText);
                        cacheFor = LeaderboardCache<string, PersonalBestInfo>.SessionLifetime;
                    }
                    else if (status == PersonalBestParseStatus.Invalid)
                    {
                        _log.Warn($"{source} personal best for {level} could not be read: {error}");
                        info = Unavailable(source, UnavailableText);
                        cacheFor = LeaderboardDataService.ErrorRetryDelay;
                    }
                    else
                    {
                        _log.Debug($"{source} personal best for {level}: {info.Accuracy.ToString("P2", CultureInfo.InvariantCulture)}, score {info.Score}.");
                        cacheFor = LeaderboardCache<string, PersonalBestInfo>.SessionLifetime;
                    }

                    break;
                case LeaderboardRequestOutcome.NotFound:
                    // TODO not sure a 404 always means no score
                    info = Unavailable(source, NoPersonalBestText);
                    cacheFor = LeaderboardCache<string, PersonalBestInfo>.SessionLifetime;
                    break;
                case LeaderboardRequestOutcome.Dropped:
                    info = Unavailable(source, UnavailableText);
                    cacheFor = null;
                    break;
                default:
                    info = Unavailable(source, UnavailableText);
                    cacheFor = LeaderboardDataService.ErrorRetryDelay;
                    break;
            }

            if (cacheFor.HasValue && generation == _cacheGeneration)
            {
                _cache.Set(key, info, cacheFor.Value);
            }

            return info;
        }

        private long? MaxScoreHint(LevelIdentity level, PbSource source)
        {
            if (!_leaderboards.TryGetCached(level, out LeaderboardDataResult data))
            {
                return null;
            }

            long max = source == PbSource.BeatLeader ? data.BeatLeader?.MaxScore ?? 0 : data.ScoreSaber?.MaxScore ?? 0;
            return max > 0 ? max : (long?)null;
        }

        internal static string StatusText(PlayerAccount account)
        {
            switch (account.Status)
            {
                case PlayerAccountStatus.Detecting: return DetectingAccountText;
                case PlayerAccountStatus.NotFound: return NoAccountText;
                case PlayerAccountStatus.Ambiguous: return AmbiguousAccountText;
                case PlayerAccountStatus.Disabled: return DisabledText;
                case PlayerAccountStatus.Found: return NoAccountText;
                default: return UnavailableText;
            }
        }

        private static PersonalBestInfo Unavailable(PbSource source, string status) =>
            new PersonalBestInfo { Available = false, Source = source, StatusText = status };

        private static void Deliver(Action<PersonalBestInfo> listener, PersonalBestInfo info)
        {
            var copy = new PersonalBestInfo
            {
                Available = info.Available,
                Source = info.Source,
                Score = info.Score,
                Accuracy = info.Accuracy,
                MaxCombo = info.MaxCombo,
                FullCombo = info.FullCombo,
                StatusText = info.StatusText,
                ScoreId = info.ScoreId,
            };
            Safe.Invoke("RemotePb.Callback", () => listener(copy));
        }

        private sealed class PendingRequest
        {
            public PendingRequest(int generation, int clearCount, Action<PersonalBestInfo> firstListener)
            {
                Generation = generation;
                ClearCount = clearCount;
                Listeners = new List<Action<PersonalBestInfo>> { firstListener };
            }

            public int Generation { get; }

            public int ClearCount { get; }

            public List<Action<PersonalBestInfo>> Listeners { get; }
        }
    }
}
