using System;
using System.Collections.Generic;
using NotCounters.Core.Leaderboards;
using NotCounters.Core.Pp;
using NotCounters.Core.Scoring;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Preview
{
    public sealed class PreviewPlay
    {
        public const float MapLengthSeconds = 180f;

        public const float SnapshotSongTime = 96f;

        public const int Seed = 0x48554453;

        public const double LocalPersonalBestAccuracy = 0.9604;

        public const double BeatLeaderPersonalBestAccuracy = 0.9561;

        public const double ScoreSaberPersonalBestAccuracy = 0.9547;

        private const float FirstNoteTime = 2f;
        private const float LastNoteMargin = 2f;
        private const float MinNoteGap = 0.2f;
        private const float NoteGapRange = 0.14f;
        private const double DoubleNoteChance = 0.15;
        private const double SameHandChance = 0.2;
        private const int NotesPerChain = 31;
        private const int LinksPerChain = 4;
        private const float ChainLinkSpacing = 0.035f;
        private const float ChainDuration = 0.5f;
        private const float SecondsPerBomb = 12f;

        private const double BaseQuality = 0.981;
        private const double HandQualityOffset = 0.002;
        private const double CenterShareOfLoss = 0.8;
        private const double PostShareOfRemainingLoss = 0.6;
        private const double PersonalBestComboShare = 0.62;

        private const int ScriptedMissIndex = 57;
        private const int ScriptedBadCutIndex = 131;

        private const float InitialEnergy = 0.5f;
        private const float GoodNoteCharge = 0.01f;
        private const float GoodChainLinkCharge = 0.002f;
        private const float BadNoteDrain = 0.1f;
        private const float MissNoteDrain = 0.15f;

        private readonly StatsSnapshot _snapshot;
        private readonly PpTracker _ppTracker;

        public PreviewPlay(StatsSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

            var random = new Random(Seed);
            BeatmapNoteSummary summary = BuildMap(random);
            IReadOnlyList<NoteSpec> notes = summary.Notes;
            ColorNotes = summary.TotalNotes;
            NoteSpanSeconds = notes.Count > 1 ? notes[notes.Count - 1].Time - notes[0].Time : 0d;
            FullMapMaxMultipliedScore = summary.FullMapMaxMultipliedScore;

            var scoreTracker = new ScoreTracker(summary, snapshot);
            new Run(random, notes, summary.TotalBombs, scoreTracker, snapshot).PlayTo(SnapshotSongTime);
            snapshot.SongLength = MapLengthSeconds;

            _ppTracker = new PpTracker(snapshot);
            _ppTracker.SetModifiers(ModifierSet.None);

            int scoredObjects = summary.TotalNotes + summary.TotalChainLinks;
            SetPersonalBest(snapshot.PersonalBest, PbSource.Local, LocalPersonalBestAccuracy, scoredObjects);
            SetPersonalBest(snapshot.BeatLeaderPersonalBest, PbSource.BeatLeader, BeatLeaderPersonalBestAccuracy, scoredObjects);
            SetPersonalBest(snapshot.ScoreSaberPersonalBest, PbSource.ScoreSaber, ScoreSaberPersonalBestAccuracy, scoredObjects);
        }

        public int ColorNotes { get; }

        public double NoteSpanSeconds { get; }

        public int FullMapMaxMultipliedScore { get; }

        public void SetLeaderboards(BeatLeaderDifficulty? beatLeader, ScoreSaberLeaderboard? scoreSaber)
        {
            if (beatLeader != null)
            {
                _ppTracker.SetBeatLeader(beatLeader, PpStatus.Loading);
            }
            else
            {
                _ppTracker.SetBeatLeader(null, PpStatus.Disabled);
            }

            if (scoreSaber != null)
            {
                _ppTracker.SetScoreSaber(scoreSaber, ScoreSaberCurves.Snapshot, scoreSaber.MaxPp, false, PpStatus.Loading);
            }
            else
            {
                _ppTracker.SetScoreSaber(null, null, null, false, PpStatus.Disabled);
            }

            SetAvailable(_snapshot.BeatLeaderPersonalBest, beatLeader != null);
            SetAvailable(_snapshot.ScoreSaberPersonalBest, scoreSaber != null);
        }

        private static BeatmapNoteSummary BuildMap(Random random)
        {
            var notes = new List<NoteSpec>(1024);
            float end = MapLengthSeconds - LastNoteMargin;
            float time = FirstNoteTime;
            Hand hand = Hand.Right;
            int index = 0;
            while (time < end)
            {
                if (index % NotesPerChain == NotesPerChain - 1)
                {
                    notes.Add(new NoteSpec(time, NoteScoringKind.ChainHead, hand));
                    for (int link = 1; link <= LinksPerChain; link++)
                    {
                        notes.Add(new NoteSpec(time + link * ChainLinkSpacing, NoteScoringKind.ChainLink, hand));
                    }

                    time += ChainDuration;
                }
                else
                {
                    notes.Add(new NoteSpec(time, NoteScoringKind.Normal, hand));
                    if (random.NextDouble() < DoubleNoteChance)
                    {
                        notes.Add(new NoteSpec(time, NoteScoringKind.Normal, Opposite(hand)));
                    }

                    time += MinNoteGap + (float)random.NextDouble() * NoteGapRange;
                }

                if (random.NextDouble() >= SameHandChance) hand = Opposite(hand);
                index++;
            }

            int bombs = (int)(Math.Max(0f, end - FirstNoteTime) / SecondsPerBomb);
            return BeatmapNoteSummary.Create(notes, bombs, 0f);
        }

        private static Hand Opposite(Hand hand) => hand == Hand.Left ? Hand.Right : Hand.Left;

        private void SetPersonalBest(PersonalBestInfo? target, PbSource source, double accuracy, int scoredObjects)
        {
            if (target == null) return;
            target.Available = true;
            target.Source = source;
            target.Accuracy = accuracy;
            target.Score = (int)Math.Round(accuracy * FullMapMaxMultipliedScore);
            target.MaxCombo = (int)(scoredObjects * PersonalBestComboShare);
            target.FullCombo = false;
            target.StatusText = null;
        }

        private static void SetAvailable(PersonalBestInfo? target, bool available)
        {
            if (target == null) return;
            target.Available = available;
            target.StatusText = available ? null : PersonalBestStatus.Disabled;
        }

        private sealed class Run
        {
            private readonly Random _random;
            private readonly IReadOnlyList<NoteSpec> _notes;
            private readonly int _totalBombs;
            private readonly ScoreTracker _scoreTracker;
            private readonly StatsSnapshot _snapshot;
            private readonly MultiplierState _multiplier = new MultiplierState();
            private readonly MultiplierState _fcMultiplier = new MultiplierState();
            private readonly NoteResult _result = new NoteResult();

            private int _multipliedScore;
            private int _immediateMaxScore;
            private int _combo;
            private int _maxCombo;
            private float _energy = InitialEnergy;

            public Run(Random random, IReadOnlyList<NoteSpec> notes, int totalBombs, ScoreTracker scoreTracker, StatsSnapshot snapshot)
            {
                _random = random;
                _notes = notes;
                _totalBombs = totalBombs;
                _scoreTracker = scoreTracker;
                _snapshot = snapshot;
            }

            public void PlayTo(float songTime)
            {
                int nextNote = 0;
                int nextBomb = 0;
                while (true)
                {
                    float noteTime = nextNote < _notes.Count ? _notes[nextNote].Time : float.PositiveInfinity;
                    float bombTime = nextBomb < _totalBombs ? BombTime(nextBomb) : float.PositiveInfinity;
                    if (Math.Min(noteTime, bombTime) > songTime) break;

                    if (noteTime <= bombTime)
                    {
                        ResolveNote(_notes[nextNote], nextNote);
                        nextNote++;
                    }
                    else
                    {
                        _scoreTracker.OnBombResolved();
                        nextBomb++;
                    }
                }

                _snapshot.SongTime = songTime;

                _scoreTracker.SetScores(_multipliedScore, _multipliedScore, _immediateMaxScore, _immediateMaxScore);
                _scoreTracker.SetCombo(_combo, _maxCombo);
                _scoreTracker.SetMultiplier(_multiplier.Multiplier, _multiplier.NormalizedProgress);
                _scoreTracker.SetEnergy(_energy, false, 0, 0, songTime, false);
                _scoreTracker.UpdateDerived();
            }

            private static float BombTime(int index) => FirstNoteTime + (index + 0.5f) * SecondsPerBomb;

            private void ResolveNote(NoteSpec note, int index)
            {
                bool isLink = ScoreDefinitions.IsChainLink(note.Kind);
                NoteResultKind kind = index == ScriptedMissIndex
                    ? NoteResultKind.Miss
                    : index == ScriptedBadCutIndex ? NoteResultKind.BadCut : NoteResultKind.GoodCut;

                _fcMultiplier.ProcessHit();
                if (kind == NoteResultKind.GoodCut) _multiplier.ProcessHit();
                else _multiplier.ProcessBreak();

                NoteResult result = _result;
                result.Kind = kind;
                result.Scoring = note.Kind;
                result.Hand = note.Hand;
                result.SongTime = note.Time;
                result.MaxCutScore = ScoreDefinitions.MaxCutScore(note.Kind);
                result.Multiplier = _multiplier.Multiplier;
                result.FcMultiplier = _fcMultiplier.Multiplier;

                if (kind == NoteResultKind.GoodCut)
                {
                    FillGoodCut(result, note);
                    _combo++;
                    if (_combo > _maxCombo) _maxCombo = _combo;
                    ChangeEnergy(isLink ? GoodChainLinkCharge : GoodNoteCharge);
                }
                else
                {
                    result.BeforeCutScore = 0;
                    result.AfterCutScore = 0;
                    result.CenterScore = 0;
                    result.CutScore = 0;
                    result.TimeDeviation = float.NaN;
                    result.SaberSpeed = float.NaN;
                    _combo = 0;
                    ChangeEnergy(kind == NoteResultKind.Miss ? -MissNoteDrain : -BadNoteDrain);
                }

                _multipliedScore += result.CutScore * result.Multiplier;
                _immediateMaxScore += result.MaxCutScore * result.FcMultiplier;

                _scoreTracker.OnNoteStarted(note.Kind, kind);
                _scoreTracker.OnNoteFinished(result);
            }

            private void FillGoodCut(NoteResult result, NoteSpec note)
            {
                NoteScoringKind kind = note.Kind;
                int beforeMax = ScoreDefinitions.MaxBeforeCutScore(kind);
                int afterMax = ScoreDefinitions.MaxAfterCutScore(kind);
                int centerMax = ScoreDefinitions.MaxCenterScore(kind);
                int beforeVariable = ScoreDefinitions.HasVariableBeforeCutScore(kind) ? beforeMax : 0;
                int afterVariable = ScoreDefinitions.HasVariableAfterCutScore(kind) ? afterMax : 0;
                int variableMax = beforeVariable + afterVariable + centerMax;

                int loss = 0;
                if (variableMax > 0)
                {
                    double meanLoss = (1d - Quality(note.Time, note.Hand)) * variableMax;
                    double spread = 0.15 + 1.7 * (_random.NextDouble() + _random.NextDouble() + _random.NextDouble()) / 3d;
                    loss = Math.Min(variableMax, (int)Math.Round(meanLoss * spread));
                }

                int centerLoss = Math.Min(centerMax, (int)Math.Round(loss * CenterShareOfLoss));
                int remaining = loss - centerLoss;
                int afterLoss = Math.Min(afterVariable, (int)Math.Round(remaining * PostShareOfRemainingLoss));
                int beforeLoss = Math.Min(beforeVariable, remaining - afterLoss);

                result.BeforeCutScore = beforeMax - beforeLoss;
                result.AfterCutScore = afterMax - afterLoss;
                result.CenterScore = centerMax - centerLoss;
                result.CutScore = result.BeforeCutScore + result.AfterCutScore + result.CenterScore + ScoreDefinitions.FixedCutScore(kind);

                bool left = note.Hand == Hand.Left;
                result.TimeDeviation = (float)Gaussian(left ? 0.006 : -0.003, left ? 0.016 : 0.014);
                double speed = Math.Max(2.5, Gaussian(left ? 11.5 : 12.6, left ? 1.6 : 1.5));
                result.SaberSpeed = (float)(ScoreDefinitions.IsChainLink(kind) ? speed * 0.6 : speed);
            }

            private static double Quality(float time, Hand hand)
            {
                double drift = 0.007 * Math.Sin(2d * Math.PI * time / 53d) + 0.004 * Math.Sin(2d * Math.PI * time / 17d + 1.3);
                double handOffset = hand == Hand.Right ? HandQualityOffset : -HandQualityOffset;
                return BaseQuality + drift + handOffset;
            }

            private void ChangeEnergy(float delta) => _energy = Math.Max(0f, Math.Min(1f, _energy + delta));

            private double Gaussian(double mean, double standardDeviation)
            {
                double u1 = 1d - _random.NextDouble();
                double u2 = _random.NextDouble();
                return mean + standardDeviation * Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2);
            }
        }
    }
}
