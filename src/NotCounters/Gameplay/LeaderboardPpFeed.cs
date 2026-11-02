using NotCounters.Contracts;
using NotCounters.Core.Pp;
using NotCounters.Core.Scoring;
using NotCounters.Core.Stats;

namespace NotCounters.Gameplay
{
    internal sealed class LeaderboardPpFeed
    {
        private readonly PpTracker _tracker;
        private readonly bool _beatLeaderEnabled;
        private readonly bool _scoreSaberEnabled;

        private bool _beatLeaderApplied;
        private BeatLeaderDifficulty? _appliedBeatLeader;
        private PpStatus _appliedBeatLeaderStatus;

        private bool _scoreSaberApplied;
        private ScoreSaberLeaderboard? _appliedScoreSaber;
        private ScoreSaberCurves? _appliedCurves;
        private double? _appliedMaxPp;
        private bool _appliedMaxPpApproximate;
        private PpStatus _appliedScoreSaberStatus;

        public LeaderboardPpFeed(PpTracker tracker, bool beatLeaderEnabled, bool scoreSaberEnabled)
        {
            _tracker = tracker;
            _beatLeaderEnabled = beatLeaderEnabled;
            _scoreSaberEnabled = scoreSaberEnabled;
            SetStatus(PpStatus.Loading);
        }

        public bool AnyEnabled => _beatLeaderEnabled || _scoreSaberEnabled;

        public bool SetStatus(PpStatus status)
        {
            bool changed = ApplyBeatLeader(null, status);
            changed |= ApplyScoreSaber(null, null, null, false, status);
            return changed;
        }

        public bool Apply(LeaderboardDataResult result)
        {
            bool changed = ApplyBeatLeader(
                result.BeatLeaderStatus == LeaderboardFetchStatus.Success ? result.BeatLeader : null,
                ToPpStatus(result.BeatLeaderStatus));

            bool hasScoreSaber = result.ScoreSaberStatus == LeaderboardFetchStatus.Success && result.ScoreSaber != null;
            changed |= ApplyScoreSaber(
                hasScoreSaber ? result.ScoreSaber : null,
                hasScoreSaber ? result.ScoreSaberCurves : null,
                hasScoreSaber ? result.ScoreSaberMaxPp : null,
                hasScoreSaber && result.ScoreSaberMaxPpApproximate,
                ToPpStatus(result.ScoreSaberStatus));
            return changed;
        }

        public static PpStatus ToPpStatus(LeaderboardFetchStatus status)
        {
            switch (status)
            {
                case LeaderboardFetchStatus.Pending: return PpStatus.Loading;
                case LeaderboardFetchStatus.Success:
                case LeaderboardFetchStatus.NotFound: return PpStatus.NotFound;
                case LeaderboardFetchStatus.Disabled: return PpStatus.Disabled;
                default: return PpStatus.Error;
            }
        }

        private bool ApplyBeatLeader(BeatLeaderDifficulty? difficulty, PpStatus statusIfNoData)
        {
            if (!_beatLeaderEnabled)
            {
                difficulty = null;
                statusIfNoData = PpStatus.Disabled;
            }

            if (_beatLeaderApplied && ReferenceEquals(difficulty, _appliedBeatLeader) && statusIfNoData == _appliedBeatLeaderStatus)
            {
                return false;
            }

            _tracker.SetBeatLeader(difficulty, statusIfNoData);
            _beatLeaderApplied = true;
            _appliedBeatLeader = difficulty;
            _appliedBeatLeaderStatus = statusIfNoData;
            return true;
        }

        private bool ApplyScoreSaber(
            ScoreSaberLeaderboard? leaderboard,
            ScoreSaberCurves? curves,
            double? maxPp,
            bool maxPpApproximate,
            PpStatus statusIfNoData)
        {
            if (!_scoreSaberEnabled)
            {
                leaderboard = null;
                curves = null;
                maxPp = null;
                maxPpApproximate = false;
                statusIfNoData = PpStatus.Disabled;
            }

            if (_scoreSaberApplied
                && ReferenceEquals(leaderboard, _appliedScoreSaber)
                && ReferenceEquals(curves, _appliedCurves)
                && maxPp == _appliedMaxPp
                && maxPpApproximate == _appliedMaxPpApproximate
                && statusIfNoData == _appliedScoreSaberStatus)
            {
                return false;
            }

            _tracker.SetScoreSaber(leaderboard, curves, maxPp, maxPpApproximate, statusIfNoData);
            _scoreSaberApplied = true;
            _appliedScoreSaber = leaderboard;
            _appliedCurves = curves;
            _appliedMaxPp = maxPp;
            _appliedMaxPpApproximate = maxPpApproximate;
            _appliedScoreSaberStatus = statusIfNoData;
            return true;
        }
    }
}
