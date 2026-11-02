namespace NotCounters.Core.Scoring
{
    public static class ScoreDefinitions
    {
        public const int FullNoteMaxCutScore = 115;

        public const int MaxPreSwingScore = 70;

        public const int MaxPostSwingScore = 30;

        public const int MaxCenterDistanceScore = 15;

        public static int MaxCutScore(NoteScoringKind kind)
        {
            Definition d = Get(kind);
            return d.CenterMax + d.BeforeMax + d.AfterMax + d.Fixed;
        }

        public static int MaxBeforeCutScore(NoteScoringKind kind) => Get(kind).BeforeMax;

        public static int MaxAfterCutScore(NoteScoringKind kind) => Get(kind).AfterMax;

        public static int MaxCenterScore(NoteScoringKind kind) => Get(kind).CenterMax;

        public static int FixedCutScore(NoteScoringKind kind) => Get(kind).Fixed;

        public static bool HasVariableBeforeCutScore(NoteScoringKind kind)
        {
            Definition d = Get(kind);
            return d.BeforeMax > 0 && d.BeforeMin != d.BeforeMax;
        }

        public static bool HasVariableAfterCutScore(NoteScoringKind kind)
        {
            Definition d = Get(kind);
            return d.AfterMax > 0 && d.AfterMin != d.AfterMax;
        }

        public static bool IsChainLink(NoteScoringKind kind) =>
            kind == NoteScoringKind.ChainLink || kind == NoteScoringKind.ChainLinkArcHead;

        public static bool IsScored(NoteScoringKind kind) => MaxCutScore(kind) > 0;

        public static bool IsFullScoreKind(NoteScoringKind kind) => MaxCutScore(kind) == FullNoteMaxCutScore;

        private static Definition Get(NoteScoringKind kind)
        {
            switch (kind)
            {
                case NoteScoringKind.Normal: return new Definition(15, 0, 70, 0, 30, 0);
                case NoteScoringKind.ArcHead: return new Definition(15, 0, 70, 30, 30, 0);
                case NoteScoringKind.ArcTail: return new Definition(15, 70, 70, 0, 30, 0);
                case NoteScoringKind.ChainHead: return new Definition(15, 0, 70, 0, 0, 0);
                case NoteScoringKind.ChainLink: return new Definition(0, 0, 0, 0, 0, 20);
                case NoteScoringKind.ArcHeadArcTail: return new Definition(15, 70, 70, 30, 30, 0);
                case NoteScoringKind.ChainHeadArcTail: return new Definition(15, 70, 70, 30, 30, 0);
                case NoteScoringKind.ChainLinkArcHead: return new Definition(0, 0, 0, 0, 0, 20);
                case NoteScoringKind.ChainHeadArcHead: return new Definition(15, 0, 70, 30, 30, 0);
                case NoteScoringKind.ChainHeadArcHeadArcTail: return new Definition(15, 70, 70, 30, 30, 0);
                default: return default;
            }
        }

        private readonly struct Definition
        {
            public Definition(int centerMax, int beforeMin, int beforeMax, int afterMin, int afterMax, int fixedScore)
            {
                CenterMax = centerMax;
                BeforeMin = beforeMin;
                BeforeMax = beforeMax;
                AfterMin = afterMin;
                AfterMax = afterMax;
                Fixed = fixedScore;
            }

            public int CenterMax { get; }

            public int BeforeMin { get; }

            public int BeforeMax { get; }

            public int AfterMin { get; }

            public int AfterMax { get; }

            public int Fixed { get; }
        }
    }
}
