using System;

namespace NotCounters.Core.Counters
{
    public static class HudTextUniformity
    {
        public const int ItalicFlag = 2;

        public const float ChangeTolerance = 1e-5f;

        public const float MinTypographicWidthRatio = 0.25f;

        public const float MaxTypographicWidthRatio = 4f;

        public static bool IsTypographicWidthRatio(float ratio) =>
            !float.IsNaN(ratio) && !float.IsInfinity(ratio) && ratio >= MinTypographicWidthRatio && ratio <= MaxTypographicWidthRatio;

        public static int FontStyle(int authoredStyle, bool uniform) => uniform ? authoredStyle & ~ItalicFlag : authoredStyle;

        public static bool IsItalic(int style) => (style & ItalicFlag) != 0;

        public static int WithItalic(int style, bool italic) => italic ? style | ItalicFlag : style & ~ItalicFlag;

        public static float CounterWidthScale(float liveWidthRatio, bool uniform) =>
            uniform || !IsTypographicWidthRatio(liveWidthRatio) ? 1f : liveWidthRatio;

        public static float VanillaScaleX(float authoredX, float authoredY, bool uniform)
        {
            if (!uniform)
            {
                return authoredX;
            }

            float height = Math.Abs(authoredY);
            if (height < 1e-6f || float.IsNaN(height) || float.IsInfinity(height)
                || !IsTypographicWidthRatio(Math.Abs(authoredX) / height))
            {
                return authoredX;
            }

            return authoredX < 0f ? -height : height;
        }

        public static bool WidensVanillaText(float authoredX, float authoredY, bool isElementRoot) =>
            !isElementRoot && Math.Abs(VanillaScaleX(authoredX, authoredY, uniform: true) - authoredX) > ChangeTolerance;

        public static float GlyphSkew(float authoredSkew, bool uniform) => uniform ? 0f : authoredSkew;

        public static bool IsSlantedGlyph(float authoredSkew) =>
            Math.Abs(authoredSkew) > ChangeTolerance && !float.IsInfinity(authoredSkew);
    }
}
