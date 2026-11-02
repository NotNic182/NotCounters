using System;
using System.Linq;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Scoring
{
    public sealed class ScoreTracker
    {
        public const int RecentSaberSpeedCapacity = 32;

        private const float EnergyZeroThreshold = 1e-5f;

        private const float NoteMatchWindowSeconds = 1f;

        private readonly StatsSnapshot _snapshot;
        private readonly NoteSpec[] _notes;
        private readonly int[] _noteMaxCutScores;
        private readonly bool[] _resolved;

        private readonly MultiplierState _multiplier = new MultiplierState();

        private readonly MultiplierState _fcMultiplier = new MultiplierState();

        private readonly MultiplierState _scratch = new MultiplierState();

        private readonly int[] _startedBalance = new int[4];

        private int _firstUnresolved;
        private long _unresolvedMaxCutSum;
        private long _fcWeightedCutSum;
        private long _fcWeightedMaxSum;
        private int? _immediateMaxModified;
        private bool _insideWall;
        private float _wallEnterTime;
        private double _closedWallSeconds;

        public ScoreTracker(BeatmapNoteSummary summary, StatsSnapshot snapshot)
        {
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _notes = summary.Notes.ToArray();
            _noteMaxCutScores = new int[_notes.Length];
            _resolved = new bool[_notes.Length];
            for (int i = 0; i < _notes.Length; i++)
            {
                _noteMaxCutScores[i] = ScoreDefinitions.MaxCutScore(_notes[i].Kind);
                _unresolvedMaxCutSum += _noteMaxCutScores[i];
            }

            ResetSnapshot();
            RefreshDerived();
        }

        public BeatmapNoteSummary Summary { get; }

        public void OnNoteStarted(NoteScoringKind kind, NoteResultKind outcome)
        {
            if (!ScoreDefinitions.IsScored(kind) || !IsKnownOutcome(outcome)) return;

            if (_startedBalance[DisplayCategory(kind, outcome)]++ < 0) return;
            CountResolution(kind, outcome);
        }

        public void OnNoteFinished(NoteResult? result)
        {
            if (result == null) return;
            NoteScoringKind kind = result.Scoring;
            int definitionMax = ScoreDefinitions.MaxCutScore(kind);
            if (definitionMax <= 0) return;
            if (!IsKnownOutcome(result.Kind)) return;

            StatsSnapshot s = _snapshot;
            bool isChainLink = ScoreDefinitions.IsChainLink(kind);
            HandStats? hand = HandFor(result.Hand);
            MarkResolved(definitionMax, result.SongTime);

            _fcMultiplier.ProcessHit();

            if (_startedBalance[DisplayCategory(kind, result.Kind)]-- <= 0) CountResolution(kind, result.Kind);

            switch (result.Kind)
            {
                case NoteResultKind.GoodCut:
                    _multiplier.ProcessHit();
                    RecordFcCut(result, definitionMax);
                    if (!isChainLink && hand != null) RecordGoodCut(hand, kind, result);
                    break;
                case NoteResultKind.BadCut:
                    _multiplier.ProcessBreak();
                    s.IsFullCombo = false;
                    if (isChainLink)
                    {
                        s.ChainLinkBadCuts++;
                    }
                    else
                    {
                        s.BadCuts++;
                        if (hand != null) hand.BadCuts++;
                    }

                    break;
                default:
                    _multiplier.ProcessBreak();
                    s.IsFullCombo = false;
                    if (isChainLink)
                    {
                        s.ChainLinkMisses++;
                    }
                    else
                    {
                        s.Misses++;
                        if (hand != null) hand.Misses++;
                    }

                    break;
            }

            RefreshDerived();
        }

        public void OnBombHit()
        {
            _snapshot.BombsHit++;
            _snapshot.IsFullCombo = false;
            _multiplier.ProcessBreak();
            RefreshDerived();
        }

        public void OnBombResolved()
        {
            _snapshot.ResolvedBombs++;
        }

        public void OnWallEnter(float songTime)
        {
            float time = IsFinite(songTime) ? songTime : _snapshot.SongTime;
            if (_insideWall) _closedWallSeconds += Math.Max(0f, time - _wallEnterTime);
            _insideWall = true;
            _wallEnterTime = time;
            _snapshot.WallsHit++;
            _snapshot.IsFullCombo = false;
            _multiplier.ProcessBreak();
            RefreshDerived();
        }

        public void OnWallExit(float songTime)
        {
            if (!_insideWall) return;
            float time = IsFinite(songTime) ? songTime : _snapshot.SongTime;
            _closedWallSeconds += Math.Max(0f, time - _wallEnterTime);
            _insideWall = false;
            RefreshDerived();
        }

        public void OnPause()
        {
            _snapshot.Pauses++;
        }

        public void SetScores(int multiplied, int modified, int immediateMaxMultiplied)
        {
            _immediateMaxModified = null;
            ApplyScores(multiplied, modified, immediateMaxMultiplied);
        }

        public void SetScores(int multiplied, int modified, int immediateMaxMultiplied, int immediateMaxModified)
        {
            _immediateMaxModified = Math.Max(0, immediateMaxModified);
            ApplyScores(multiplied, modified, immediateMaxMultiplied);
        }

        public void SetCombo(int combo, int maxCombo)
        {
            _snapshot.Combo = Math.Max(0, combo);
            _snapshot.MaxCombo = Math.Max(_snapshot.MaxCombo, Math.Max(_snapshot.Combo, maxCombo));
        }

        public void SetMultiplier(int multiplier, float progress)
        {
            _snapshot.Multiplier = NormalizeMultiplier(multiplier);
            _snapshot.MultiplierProgress = float.IsNaN(progress) ? 0f : Math.Min(1f, Math.Max(0f, progress));
        }

        public void SetEnergy(float energy, bool battery, int lives, int maxLives, float songTime, bool levelFailed)
        {
            StatsSnapshot s = _snapshot;
            if (!float.IsNaN(energy)) s.Energy = Math.Min(1f, Math.Max(0f, energy));
            s.IsBatteryEnergy = battery;
            s.BatteryLives = Math.Max(0, lives);
            s.BatteryMaxLives = Math.Max(s.BatteryLives, maxLives);
            if (!s.EnergyReachedZero && !float.IsNaN(energy) && energy <= EnergyZeroThreshold)
            {
                s.EnergyReachedZero = true;
                s.FailedAtSongTime = IsFinite(songTime) ? songTime : s.SongTime;
            }

            if (levelFailed) s.LevelFailed = true;
        }

        public void UpdateDerived()
        {
            RefreshDerived();
        }

        private void ApplyScores(int multiplied, int modified, int immediateMaxMultiplied)
        {
            _snapshot.MultipliedScore = Math.Max(0, multiplied);
            _snapshot.ModifiedScore = Math.Max(0, modified);
            _snapshot.ImmediateMaxMultipliedScore = Math.Max(0, immediateMaxMultiplied);
            RefreshDerived();
        }

        private void RefreshDerived()
        {
            StatsSnapshot s = _snapshot;
            int immediateMax = s.ImmediateMaxMultipliedScore;
            if (immediateMax > 0)
            {
                s.Accuracy = Clamp01((double)s.MultipliedScore / immediateMax);
                s.ModifiedRelativeScore = (double)s.ModifiedScore / immediateMax;
                s.Rank = RankCalculator.GetImmediateRankName(s.MultipliedScore, s.ModifiedScore, immediateMax, ImmediateMaxModified());
            }
            else
            {
                s.Accuracy = null;
                s.ModifiedRelativeScore = null;
                s.Rank = null;
            }

            s.FcAccuracy = _fcWeightedMaxSum > 0 ? Clamp01((double)_fcWeightedCutSum / _fcWeightedMaxSum) : (double?)null;

            int fullMax = Summary.FullMapMaxMultipliedScore;
            s.BestPossibleAccuracy = fullMax > 0
                ? Clamp01((s.MultipliedScore + ComputeMaxRemaining()) / (double)fullMax)
                : (double?)null;

            double open = _insideWall && IsFinite(s.SongTime) ? Math.Max(0f, s.SongTime - _wallEnterTime) : 0;
            s.SecondsInWalls = (float)(_closedWallSeconds + open);
        }

        private int ImmediateMaxModified()
        {
            if (_immediateMaxModified.HasValue) return _immediateMaxModified.Value;
            StatsSnapshot s = _snapshot;
            if (s.MultipliedScore <= 0) return s.ImmediateMaxMultipliedScore;
            double estimate = Math.Floor(s.ImmediateMaxMultipliedScore * ((double)s.ModifiedScore / s.MultipliedScore));
            return estimate >= int.MaxValue ? int.MaxValue : (int)estimate;
        }

        private long ComputeMaxRemaining()
        {
            _scratch.CopyFrom(_multiplier);
            long total = 0;
            long walkedMaxCut = 0;
            for (int i = _firstUnresolved; i < _notes.Length && _scratch.Multiplier < MultiplierState.MaxMultiplier; i++)
            {
                if (_resolved[i]) continue;
                int max = _noteMaxCutScores[i];
                _scratch.ProcessHit();
                total += (long)max * _scratch.Multiplier;
                walkedMaxCut += max;
            }

            return total + (_unresolvedMaxCutSum - walkedMaxCut) * MultiplierState.MaxMultiplier;
        }

        private void RecordFcCut(NoteResult result, int definitionMax)
        {
            int max = result.MaxCutScore > 0 ? result.MaxCutScore : definitionMax;
            int cut = Clamp(result.CutScore, 0, max);
            int fcMultiplier = result.FcMultiplier > 0
                ? Math.Min(result.FcMultiplier, MultiplierState.MaxMultiplier)
                : _fcMultiplier.Multiplier;
            _fcWeightedCutSum += (long)cut * fcMultiplier;
            _fcWeightedMaxSum += (long)max * fcMultiplier;
        }

        private static void RecordGoodCut(HandStats hand, NoteScoringKind kind, NoteResult result)
        {
            hand.GoodCuts++;

            if (ScoreDefinitions.IsFullScoreKind(kind))
            {
                hand.CutScoreSum += Clamp(result.CutScore, 0, ScoreDefinitions.FullNoteMaxCutScore);
                hand.CutScoreCount++;
            }

            if (ScoreDefinitions.HasVariableBeforeCutScore(kind))
            {
                int maxPre = ScoreDefinitions.MaxBeforeCutScore(kind);
                int pre = Clamp(result.BeforeCutScore, 0, maxPre);
                hand.PreSwingSum += pre;
                hand.PreSwingCount++;
                if (pre >= maxPre) hand.FullPreSwings++;
            }

            if (ScoreDefinitions.HasVariableAfterCutScore(kind))
            {
                int maxPost = ScoreDefinitions.MaxAfterCutScore(kind);
                int post = Clamp(result.AfterCutScore, 0, maxPost);
                hand.PostSwingSum += post;
                hand.PostSwingCount++;
                if (post >= maxPost) hand.FullPostSwings++;
            }

            int maxCenter = ScoreDefinitions.MaxCenterScore(kind);
            if (maxCenter > 0)
            {
                hand.CenterSum += Clamp(result.CenterScore, 0, maxCenter);
                hand.CenterCount++;
            }

            if (IsFinite(result.TimeDeviation))
            {
                hand.TimingDeviationSum += result.TimeDeviation;
                hand.TimingDeviationAbsSum += Math.Abs(result.TimeDeviation);
                hand.TimingCount++;
            }

            if (IsFinite(result.SaberSpeed) && result.SaberSpeed >= 0)
            {
                hand.SaberSpeedSum += result.SaberSpeed;
                hand.SaberSpeedCount++;
                hand.RecentSaberSpeeds.Add(result.SaberSpeed);
                if (hand.RecentSaberSpeeds.Count > RecentSaberSpeedCapacity)
                {
                    hand.RecentSaberSpeeds.RemoveRange(0, hand.RecentSaberSpeeds.Count - RecentSaberSpeedCapacity);
                }
            }
        }

        private void CountResolution(NoteScoringKind kind, NoteResultKind outcome)
        {
            StatsSnapshot s = _snapshot;
            bool good = outcome == NoteResultKind.GoodCut;
            if (ScoreDefinitions.IsChainLink(kind))
            {
                s.ResolvedChainLinks++;
                if (good) s.ChainLinkGoodCuts++;
            }
            else
            {
                s.ResolvedNotes++;
                if (good) s.GoodCuts++;
            }
        }

        private static int DisplayCategory(NoteScoringKind kind, NoteResultKind outcome) =>
            (ScoreDefinitions.IsChainLink(kind) ? 2 : 0) + (outcome == NoteResultKind.GoodCut ? 1 : 0);

        private static bool IsKnownOutcome(NoteResultKind outcome) =>
            outcome == NoteResultKind.GoodCut || outcome == NoteResultKind.BadCut || outcome == NoteResultKind.Miss;

        private void MarkResolved(int maxCutScore, float noteTime)
        {
            int index = FindUnresolvedNote(maxCutScore, noteTime);
            if (index < 0) return;
            _resolved[index] = true;
            _unresolvedMaxCutSum -= _noteMaxCutScores[index];
            while (_firstUnresolved < _notes.Length && _resolved[_firstUnresolved]) _firstUnresolved++;
        }

        private int FindUnresolvedNote(int maxCutScore, float noteTime)
        {
            if (_firstUnresolved >= _notes.Length) return -1;
            if (IsFinite(noteTime))
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = Math.Max(_firstUnresolved, LowerBound(noteTime - NoteMatchWindowSeconds));
                     i < _notes.Length && _notes[i].Time <= noteTime + NoteMatchWindowSeconds;
                     i++)
                {
                    if (_resolved[i] || _noteMaxCutScores[i] != maxCutScore) continue;
                    float distance = Math.Abs(_notes[i].Time - noteTime);
                    if (distance < bestDistance)
                    {
                        best = i;
                        bestDistance = distance;
                    }
                }

                if (best >= 0) return best;
            }

            for (int i = _firstUnresolved; i < _notes.Length; i++)
            {
                if (!_resolved[i] && _noteMaxCutScores[i] == maxCutScore) return i;
            }

            return -1;
        }

        private int LowerBound(float time)
        {
            int lo = 0, hi = _notes.Length;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (_notes[mid].Time < time) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        private HandStats? HandFor(Hand hand)
        {
            switch (hand)
            {
                case Hand.Left: return _snapshot.Left;
                case Hand.Right: return _snapshot.Right;
                default: return null;
            }
        }

        private void ResetSnapshot()
        {
            StatsSnapshot s = _snapshot;
            s.TotalNotes = Summary.TotalNotes;
            s.TotalChainLinks = Summary.TotalChainLinks;
            s.TotalBombs = Summary.TotalBombs;
            s.FullMapMaxMultipliedScore = Summary.FullMapMaxMultipliedScore;
            s.MultipliedScore = 0;
            s.ModifiedScore = 0;
            s.ImmediateMaxMultipliedScore = 0;
            s.Combo = 0;
            s.MaxCombo = 0;
            s.IsFullCombo = true;
            s.Multiplier = 1;
            s.MultiplierProgress = 0;
            s.ResolvedNotes = 0;
            s.ResolvedChainLinks = 0;
            s.GoodCuts = 0;
            s.ChainLinkGoodCuts = 0;
            s.Misses = 0;
            s.BadCuts = 0;
            s.ChainLinkMisses = 0;
            s.ChainLinkBadCuts = 0;
            s.BombsHit = 0;
            s.ResolvedBombs = 0;
            s.WallsHit = 0;
            s.SecondsInWalls = 0;
            s.Pauses = 0;
            s.EnergyReachedZero = false;
            s.FailedAtSongTime = null;
            s.LevelFailed = false;
            ResetHand(s.Left);
            ResetHand(s.Right);
        }

        private static void ResetHand(HandStats hand)
        {
            hand.GoodCuts = 0;
            hand.Misses = 0;
            hand.BadCuts = 0;
            hand.CutScoreSum = 0;
            hand.CutScoreCount = 0;
            hand.PreSwingSum = 0;
            hand.PreSwingCount = 0;
            hand.PostSwingSum = 0;
            hand.PostSwingCount = 0;
            hand.CenterSum = 0;
            hand.CenterCount = 0;
            hand.FullPreSwings = 0;
            hand.FullPostSwings = 0;
            hand.TimingDeviationSum = 0;
            hand.TimingDeviationAbsSum = 0;
            hand.TimingCount = 0;
            hand.SaberSpeedSum = 0;
            hand.SaberSpeedCount = 0;
            hand.RecentSaberSpeeds.Clear();
        }

        private static int NormalizeMultiplier(int multiplier)
        {
            if (multiplier >= 8) return 8;
            if (multiplier >= 4) return 4;
            if (multiplier >= 2) return 2;
            return 1;
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
