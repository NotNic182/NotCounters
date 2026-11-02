using System;
using System.Collections.Generic;
using NotCounters.Core.Leaderboards;
using NotCounters.Core.Pp;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Scoring
{
    public sealed class PpTracker
    {
        private readonly StatsSnapshot _snapshot;

        private BeatLeaderDifficulty? _beatLeader;
        private PpStatus _beatLeaderStatusIfNoData = PpStatus.Loading;
        private BeatLeaderPpContext? _context;
        private BeatLeaderDifficulty? _contextDifficulty;
        private string? _contextCodes;

        private ScoreSaberLeaderboard? _scoreSaber;
        private ScoreSaberCurves? _scoreSaberCurves;
        private double? _scoreSaberMaxPp;
        private bool _scoreSaberMaxPpApproximate;
        private PpStatus _scoreSaberStatusIfNoData = PpStatus.Loading;

        private ModifierSet? _codesModifiers;
        private bool _codesNoFailFromStart;
        private bool _codesEnergyReachedZero;
        private string _beatLeaderCodes = string.Empty;
        private IReadOnlyList<string> _scoreSaberCodes = Array.Empty<string>();
        private ScoreSaberLeaderboard? _multiplierLeaderboard;
        private double _scoreSaberMultiplier = 1;

        private bool _computed;
        private double? _computedAccuracy;
        private double? _computedFcAccuracy;
        private bool _computedEnergyReachedZero;
        private bool _computedLevelFailed;
        private bool _computedNoFailCountsAsZero;
        private ModifierSet? _computedModifiers;
        private LeaderboardPpInfo? _computedBeatLeader;
        private LeaderboardPpInfo? _computedScoreSaber;

        public PpTracker(StatsSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public bool BeatLeaderNoFailCountsAsZero { get; set; }

        public string? LastError { get; private set; }

        public void SetModifiers(ModifierSet? modifiers)
        {
            _snapshot.Modifiers = modifiers ?? ModifierSet.None;
            Compute();
        }

        public void SetBeatLeader(BeatLeaderDifficulty? difficulty, PpStatus statusIfNoData)
        {
            _beatLeader = difficulty;
            _beatLeaderStatusIfNoData = statusIfNoData;
            Compute();
        }

        public void SetScoreSaber(ScoreSaberLeaderboard? leaderboard, ScoreSaberCurves? curves, double? maxPp, bool maxPpApproximate, PpStatus statusIfNoData)
        {
            _scoreSaber = leaderboard;
            _scoreSaberCurves = curves;
            _scoreSaberMaxPp = maxPp;
            _scoreSaberMaxPpApproximate = maxPpApproximate;
            _scoreSaberStatusIfNoData = statusIfNoData;
            Compute();
        }

        public void Update()
        {
            if (_computed && InputsUnchanged()) return;
            Compute();
        }

        private void Compute()
        {
            bool complete = true;
            LeaderboardPpInfo beatLeader = _snapshot.BeatLeader ??= new LeaderboardPpInfo();
            try
            {
                UpdateBeatLeader(beatLeader);
                ApplyLevelFailed(beatLeader);
            }
            catch (Exception ex)
            {
                complete = false;
                Fail(beatLeader, "BeatLeader pp update failed: " + ex.Message);
            }

            LeaderboardPpInfo scoreSaber = _snapshot.ScoreSaber ??= new LeaderboardPpInfo();
            try
            {
                UpdateScoreSaber(scoreSaber);
                ApplyLevelFailed(scoreSaber);
            }
            catch (Exception ex)
            {
                complete = false;
                Fail(scoreSaber, "ScoreSaber pp update failed: " + ex.Message);
            }

            StatsSnapshot s = _snapshot;
            _computed = complete;
            _computedAccuracy = s.Accuracy;
            _computedFcAccuracy = s.FcAccuracy;
            _computedEnergyReachedZero = s.EnergyReachedZero;
            _computedLevelFailed = s.LevelFailed;
            _computedNoFailCountsAsZero = BeatLeaderNoFailCountsAsZero;
            _computedModifiers = s.Modifiers;
            _computedBeatLeader = beatLeader;
            _computedScoreSaber = scoreSaber;
        }

        private bool InputsUnchanged()
        {
            StatsSnapshot s = _snapshot;
            return SameValue(s.Accuracy, _computedAccuracy)
                && SameValue(s.FcAccuracy, _computedFcAccuracy)
                && s.EnergyReachedZero == _computedEnergyReachedZero
                && s.LevelFailed == _computedLevelFailed
                && BeatLeaderNoFailCountsAsZero == _computedNoFailCountsAsZero
                && ReferenceEquals(s.Modifiers, _computedModifiers)
                && ReferenceEquals(s.BeatLeader, _computedBeatLeader)
                && ReferenceEquals(s.ScoreSaber, _computedScoreSaber);
        }

        private static bool SameValue(double? a, double? b) =>
            a.HasValue
                ? b.HasValue && BitConverter.DoubleToInt64Bits(a.Value) == BitConverter.DoubleToInt64Bits(b.Value)
                : !b.HasValue;

        private void UpdateBeatLeader(LeaderboardPpInfo info)
        {
            Clear(info);
            ModifierSet modifiers = _snapshot.Modifiers ?? ModifierSet.None;
            BeatLeaderDifficulty? difficulty = _beatLeader;
            if (difficulty == null)
            {
                info.Status = StatusWithoutData(_beatLeaderStatusIfNoData, modifiers);
                return;
            }

            RefreshCodes(modifiers);
            BeatLeaderPpContext context = GetContext(difficulty, _beatLeaderCodes);
            PpAvailabilityResult availability = PpAvailability.ForBeatLeader(difficulty, context.HasStarDrift);
            if (availability.ShowsPp && context.IsValid) info.Stars = FiniteOrNull(context.DisplayStars);

            if (modifiers.IsLeaderboardIncompatible)
            {
                info.Status = PpStatus.Incompatible;
                return;
            }

            if (!availability.ShowsPp)
            {
                info.Status = ToStatus(availability.State);
                return;
            }

            if (!context.IsValid)
            {
                info.Status = PpStatus.Error;
                LastError = context.Error;
                return;
            }

            info.Status = ToStatus(availability.State);
            info.IsApproximate = IsApproximate(availability) || context.IsApproximate;

            if (_snapshot.Accuracy is double accuracy && IsFinite(accuracy))
            {
                BeatLeaderPpResult live = context.Evaluate(Clamp01(accuracy));
                info.Pp = live.Pp;
                info.AccPp = live.AccPp;
                info.PassPp = live.PassPp;
                info.TechPp = live.TechPp;
            }

            if (_snapshot.FcAccuracy is double fcAccuracy && IsFinite(fcAccuracy))
            {
                info.FcPp = context.Evaluate(Clamp01(fcAccuracy)).Pp;
            }

            info.MaxPp = context.Evaluate(1.0).Pp;
        }

        private void UpdateScoreSaber(LeaderboardPpInfo info)
        {
            Clear(info);
            ModifierSet modifiers = _snapshot.Modifiers ?? ModifierSet.None;
            ScoreSaberLeaderboard? leaderboard = _scoreSaber;
            if (leaderboard == null)
            {
                info.Status = StatusWithoutData(_scoreSaberStatusIfNoData, modifiers);
                return;
            }

            if (leaderboard.Stars > 0) info.Stars = FiniteOrNull(leaderboard.Stars);

            if (modifiers.IsLeaderboardIncompatible)
            {
                info.Status = PpStatus.Incompatible;
                return;
            }

            PpAvailabilityResult availability = PpAvailability.ForScoreSaber(leaderboard, _scoreSaberMaxPp, _scoreSaberMaxPpApproximate);
            info.Status = ToStatus(availability.State);
            if (!availability.ShowsPp) return;

            bool maxPpFromStars = !_scoreSaberMaxPp.HasValue && !leaderboard.MaxPp.HasValue;
            info.IsApproximate = IsApproximate(availability) || _scoreSaberMaxPpApproximate || maxPpFromStars;

            RefreshCodes(modifiers);
            double multiplier = ScoreSaberMultiplier(leaderboard);
            bool includeQualified = availability.State == PpDisplayState.Qualified;

            if (_snapshot.Accuracy is double accuracy && IsFinite(accuracy))
            {
                info.Pp = ScoreSaberPpCalculator.LivePpWithMultiplier(accuracy, multiplier, leaderboard, _scoreSaberCurves, _scoreSaberMaxPp, includeQualified);
            }

            if (_snapshot.FcAccuracy is double fcAccuracy && IsFinite(fcAccuracy))
            {
                info.FcPp = ScoreSaberPpCalculator.LivePpWithMultiplier(fcAccuracy, multiplier, leaderboard, _scoreSaberCurves, _scoreSaberMaxPp, includeQualified);
            }

            info.MaxPp = ScoreSaberPpCalculator.LivePpWithMultiplier(1.0, multiplier, leaderboard, _scoreSaberCurves, _scoreSaberMaxPp, includeQualified);
        }

        private void RefreshCodes(ModifierSet modifiers)
        {
            bool fromStart = BeatLeaderNoFailCountsAsZero;
            bool reachedZero = _snapshot.EnergyReachedZero;
            if (ReferenceEquals(_codesModifiers, modifiers) && _codesNoFailFromStart == fromStart && _codesEnergyReachedZero == reachedZero)
            {
                return;
            }

            string beatLeaderCodes = modifiers.ToBeatLeaderCodes(fromStart, reachedZero);
            IReadOnlyList<string> scoreSaberCodes = modifiers.ToScoreSaberCodes(reachedZero);

            _beatLeaderCodes = beatLeaderCodes;
            _scoreSaberCodes = scoreSaberCodes;
            _multiplierLeaderboard = null;
            _codesModifiers = modifiers;
            _codesNoFailFromStart = fromStart;
            _codesEnergyReachedZero = reachedZero;
        }

        private double ScoreSaberMultiplier(ScoreSaberLeaderboard leaderboard)
        {
            if (!ReferenceEquals(_multiplierLeaderboard, leaderboard))
            {
                _scoreSaberMultiplier = ScoreSaberPpCalculator.Multiplier(_scoreSaberCodes, leaderboard.PositiveModifiers, leaderboard.ModifierValues);
                _multiplierLeaderboard = leaderboard;
            }

            return _scoreSaberMultiplier;
        }

        private void ApplyLevelFailed(LeaderboardPpInfo info)
        {
            if (!_snapshot.LevelFailed || !ShowsPp(info.Status)) return;

            double? stars = info.Stars;
            Clear(info);
            info.Stars = stars;
            info.Status = PpStatus.Failed;
        }

        private static bool ShowsPp(PpStatus status) =>
            status == PpStatus.Ranked || status == PpStatus.Qualified || status == PpStatus.InEvent;

        private BeatLeaderPpContext GetContext(BeatLeaderDifficulty difficulty, string codes)
        {
            if (_context == null || !ReferenceEquals(_contextDifficulty, difficulty) || !string.Equals(_contextCodes, codes, StringComparison.Ordinal))
            {
                _context = BeatLeaderPpContext.Create(difficulty, codes);
                _contextDifficulty = difficulty;
                _contextCodes = codes;
            }

            return _context;
        }

        private static PpStatus StatusWithoutData(PpStatus statusIfNoData, ModifierSet modifiers)
        {
            if (statusIfNoData == PpStatus.Disabled) return PpStatus.Disabled;
            return modifiers.IsLeaderboardIncompatible ? PpStatus.Incompatible : statusIfNoData;
        }

        private static PpStatus ToStatus(PpDisplayState state)
        {
            switch (state)
            {
                case PpDisplayState.Loading: return PpStatus.Loading;
                case PpDisplayState.Ranked:
                case PpDisplayState.Approximate:
                    return PpStatus.Ranked;
                case PpDisplayState.Qualified: return PpStatus.Qualified;
                case PpDisplayState.InEvent: return PpStatus.InEvent;
                case PpDisplayState.Unranked: return PpStatus.Unranked;
                case PpDisplayState.Loved: return PpStatus.Loved;
                case PpDisplayState.NotFound: return PpStatus.NotFound;
                default: return PpStatus.Error;
            }
        }

        private static bool IsApproximate(PpAvailabilityResult availability) =>
            availability.State == PpDisplayState.Approximate
            || string.Equals(availability.Prefix, PpAvailability.ApproximatePrefix, StringComparison.Ordinal);

        private void Fail(LeaderboardPpInfo info, string error)
        {
            Clear(info);
            info.Status = PpStatus.Error;
            LastError = error;
        }

        private static void Clear(LeaderboardPpInfo info)
        {
            info.Pp = null;
            info.FcPp = null;
            info.MaxPp = null;
            info.Stars = null;
            info.AccPp = null;
            info.PassPp = null;
            info.TechPp = null;
            info.IsApproximate = false;
        }

        private static double? FiniteOrNull(double value) => IsFinite(value) ? value : (double?)null;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
    }
}
