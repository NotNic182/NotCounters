using NotCounters.Core.Config;

namespace NotCounters.Core.Counters
{
    public static class CounterColors
    {
        public static readonly ColorValue RankSS = FromRgb(0x4D, 0xE8, 0xFF);

        public static readonly ColorValue RankS = ColorValue.White;

        public static readonly ColorValue RankA = FromRgb(0x5E, 0xE0, 0x7A);

        public static readonly ColorValue RankB = FromRgb(0xFF, 0xE4, 0x5C);

        public static readonly ColorValue RankC = FromRgb(0xFF, 0x9F, 0x43);

        public static readonly ColorValue RankD = FromRgb(0xFF, 0x5A, 0x5A);

        public static readonly ColorValue Positive = RankA;

        public static readonly ColorValue Negative = RankD;

        public static readonly ColorValue Neutral = ColorValue.White;

        public static readonly ColorValue Danger = RankD;

        public static readonly ColorValue DefaultLeftSaber = FromRgb(0xC8, 0x14, 0x14);

        public static readonly ColorValue DefaultRightSaber = FromRgb(0x28, 0x8E, 0xD2);

        public static ColorValue ForAccuracy(double fraction)
        {
            if (double.IsNaN(fraction))
            {
                return RankD;
            }

            if (fraction > 0.9)
            {
                return RankSS;
            }

            if (fraction > 0.8)
            {
                return RankS;
            }

            if (fraction > 0.65)
            {
                return RankA;
            }

            if (fraction > 0.5)
            {
                return RankB;
            }

            return fraction > 0.35 ? RankC : RankD;
        }

        public static ColorValue? ForRank(string? rankName)
        {
            switch (rankName)
            {
                case "SSS":
                case "SS":
                    return RankSS;
                case "S": return RankS;
                case "A": return RankA;
                case "B": return RankB;
                case "C": return RankC;
                case "D":
                case "E":
                    return RankD;
                default: return null;
            }
        }

        public static ColorValue ForDelta(double displayedDelta)
        {
            if (displayedDelta > 0d)
            {
                return Positive;
            }

            return displayedDelta < 0d ? Negative : Neutral;
        }

        private static ColorValue FromRgb(byte r, byte g, byte b) => new ColorValue(r / 255f, g / 255f, b / 255f, 1f);
    }
}
