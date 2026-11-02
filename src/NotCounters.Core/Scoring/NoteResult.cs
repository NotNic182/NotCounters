namespace NotCounters.Core.Scoring
{
    public enum NoteResultKind
    {
        GoodCut,
        BadCut,
        Miss,
    }

    public sealed class NoteResult
    {
        public NoteResultKind Kind { get; set; }

        public NoteScoringKind Scoring { get; set; }

        public Hand Hand { get; set; } = Hand.Unknown;

        public int BeforeCutScore { get; set; }

        public int AfterCutScore { get; set; }

        public int CenterScore { get; set; }

        public int CutScore { get; set; }

        public int MaxCutScore { get; set; }

        public int Multiplier { get; set; }

        public int FcMultiplier { get; set; }

        public float TimeDeviation { get; set; }

        public float SaberSpeed { get; set; }

        public float SongTime { get; set; }
    }
}
