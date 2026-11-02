namespace NotCounters.Core.Scoring
{
    public enum NoteScoringKind
    {
        Ignore = -1,
        NoScore = 0,
        Normal = 1,
        ArcHead = 2,
        ArcTail = 3,
        ChainHead = 4,
        ChainLink = 5,
        ArcHeadArcTail = 6,
        ChainHeadArcTail = 7,
        ChainLinkArcHead = 8,
        ChainHeadArcHead = 9,
        ChainHeadArcHeadArcTail = 10,
    }

    public enum Hand
    {
        Left,
        Right,
        Unknown,
    }
}
