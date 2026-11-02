using NotCounters.Core.Scoring;

namespace NotCounters.Gameplay
{
    internal static class GameTypeMapping
    {
        public static NoteScoringKind ToScoringKind(NoteData.ScoringType scoringType) => (NoteScoringKind)(int)scoringType;

        public static Hand ToHand(ColorType colorType)
        {
            switch (colorType)
            {
                case ColorType.ColorA: return Hand.Left;
                case ColorType.ColorB: return Hand.Right;
                default: return Hand.Unknown;
            }
        }

        public static Hand ToHand(SaberType saberType) => saberType == SaberType.SaberA ? Hand.Left : Hand.Right;
    }
}
