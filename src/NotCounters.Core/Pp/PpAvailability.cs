using System;
using System.Globalization;
using NotCounters.Core.Leaderboards;

namespace NotCounters.Core.Pp
{
    public enum PpDisplayState
    {
        Loading,
        Ranked,
        Qualified,
        InEvent,
        Unranked,
        Loved,
        NotFound,
        Error,
        Approximate,
    }

    public enum LeaderboardFetchOutcome
    {
        Pending,
        NotFound,
        NetworkError,
        ParseError,
    }

    public readonly struct PpAvailabilityResult
    {
        public const string DefaultNumberFormat = "0.00";

        private readonly string? _label;
        private readonly string? _prefix;
        private readonly string? _suffix;

        public PpAvailabilityResult(PpDisplayState state, bool showsPp, string label, string prefix = "", string suffix = "")
        {
            State = state;
            ShowsPp = showsPp;
            _label = label;
            _prefix = prefix;
            _suffix = suffix;
        }

        public PpDisplayState State { get; }

        public bool ShowsPp { get; }

        public string Label => _label ?? string.Empty;

        public string Prefix => _prefix ?? string.Empty;

        public string Suffix => _suffix ?? string.Empty;

        public string Format(double? pp, string numberFormat = DefaultNumberFormat)
        {
            if (!ShowsPp) return Label;
            if (!pp.HasValue || double.IsNaN(pp.Value) || double.IsInfinity(pp.Value)) return "--";
            string number;
            try
            {
                number = pp.Value.ToString(string.IsNullOrEmpty(numberFormat) ? DefaultNumberFormat : numberFormat, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                number = pp.Value.ToString(DefaultNumberFormat, CultureInfo.InvariantCulture);
            }

            return Prefix + number + Suffix;
        }
    }

    public static class PpAvailability
    {
        public const string QualifiedSuffix = " (Q)";

        public const string InEventSuffix = " (E)";

        public const string ApproximatePrefix = "~";

        public static PpAvailabilityResult IncompatibleModifiers { get; } =
            new PpAvailabilityResult(PpDisplayState.Error, false, "--");

        public static string DefaultLabel(PpDisplayState state, LeaderboardService service)
        {
            switch (state)
            {
                case PpDisplayState.Loading: return "...";
                case PpDisplayState.Ranked: return "Ranked";
                case PpDisplayState.Qualified: return "Qualified";
                case PpDisplayState.InEvent: return "Event";
                case PpDisplayState.Unranked: return "Unranked";
                case PpDisplayState.Loved: return "Loved";
                case PpDisplayState.NotFound:
                    return service == LeaderboardService.BeatLeader ? "No BL leaderboard" : "No SS leaderboard";
                case PpDisplayState.Approximate: return ApproximatePrefix;
                default: return "--";
            }
        }

        public static PpAvailabilityResult ForOutcome(LeaderboardFetchOutcome outcome, LeaderboardService service)
        {
            PpDisplayState state;
            switch (outcome)
            {
                case LeaderboardFetchOutcome.Pending: state = PpDisplayState.Loading; break;
                case LeaderboardFetchOutcome.NotFound: state = PpDisplayState.NotFound; break;
                default: state = PpDisplayState.Error; break;
            }

            return NoNumber(state, service);
        }

        public static PpAvailabilityResult ForBeatLeader(LeaderboardFetchOutcome outcome) =>
            ForOutcome(outcome, LeaderboardService.BeatLeader);

        public static PpAvailabilityResult ForScoreSaber(LeaderboardFetchOutcome outcome) =>
            ForOutcome(outcome, LeaderboardService.ScoreSaber);

        public static PpAvailabilityResult ForBeatLeader(BeatLeaderDifficulty? difficulty, bool driftDetected = false)
        {
            const LeaderboardService bl = LeaderboardService.BeatLeader;
            if (difficulty == null || difficulty.Status < 0) return NoNumber(PpDisplayState.Error, bl);

            switch (difficulty.StatusKind)
            {
                case BeatLeaderStatus.Ranked:
                case BeatLeaderStatus.Qualified:
                case BeatLeaderStatus.InEvent:
                    break;
                default:
                    return NoNumber(PpDisplayState.Unranked, bl);
            }

            if (difficulty.HasNoRatings) return NoNumber(PpDisplayState.Error, bl);

            bool approximate = !difficulty.IsReweighted || driftDetected;
            string prefix = approximate ? ApproximatePrefix : string.Empty;

            switch (difficulty.StatusKind)
            {
                case BeatLeaderStatus.Qualified:
                    return new PpAvailabilityResult(PpDisplayState.Qualified, true, DefaultLabel(PpDisplayState.Qualified, bl), prefix, QualifiedSuffix);
                case BeatLeaderStatus.InEvent:
                    return new PpAvailabilityResult(PpDisplayState.InEvent, true, DefaultLabel(PpDisplayState.InEvent, bl), prefix, InEventSuffix);
                default:
                    return approximate
                        ? new PpAvailabilityResult(PpDisplayState.Approximate, true, DefaultLabel(PpDisplayState.Approximate, bl), ApproximatePrefix)
                        : new PpAvailabilityResult(PpDisplayState.Ranked, true, DefaultLabel(PpDisplayState.Ranked, bl));
            }
        }

        public static PpAvailabilityResult ForScoreSaber(ScoreSaberLeaderboard? leaderboard, double? maxPp = null, bool maxPpApproximate = false)
        {
            const LeaderboardService ss = LeaderboardService.ScoreSaber;
            if (leaderboard == null) return NoNumber(PpDisplayState.Error, ss);

            switch (leaderboard.Status)
            {
                case ScoreSaberLeaderboardStatus.Ranked:
                    if (!(leaderboard.Stars > 0)) return NoNumber(PpDisplayState.Unranked, ss);
                    double? resolved = ScoreSaberPpCalculator.ResolveMaxPp(leaderboard, maxPp);
                    if (!resolved.HasValue || !(resolved.Value > 0)) return NoNumber(PpDisplayState.Unranked, ss);
                    if (leaderboard.MaxScore <= 0) return NoNumber(PpDisplayState.Error, ss);
                    return maxPpApproximate
                        ? new PpAvailabilityResult(PpDisplayState.Approximate, true, DefaultLabel(PpDisplayState.Approximate, ss), ApproximatePrefix)
                        : new PpAvailabilityResult(PpDisplayState.Ranked, true, DefaultLabel(PpDisplayState.Ranked, ss));
                case ScoreSaberLeaderboardStatus.Qualified:
                    return leaderboard.Stars > 0 && leaderboard.MaxScore > 0
                        ? new PpAvailabilityResult(PpDisplayState.Qualified, true, DefaultLabel(PpDisplayState.Qualified, ss), ApproximatePrefix, QualifiedSuffix)
                        : NoNumber(PpDisplayState.Qualified, ss);
                case ScoreSaberLeaderboardStatus.Loved:
                    return NoNumber(PpDisplayState.Loved, ss);
                case ScoreSaberLeaderboardStatus.Unranked:
                    return NoNumber(PpDisplayState.Unranked, ss);
                default:
                    return NoNumber(PpDisplayState.Error, ss);
            }
        }

        private static PpAvailabilityResult NoNumber(PpDisplayState state, LeaderboardService service) =>
            new PpAvailabilityResult(state, false, DefaultLabel(state, service));
    }
}
