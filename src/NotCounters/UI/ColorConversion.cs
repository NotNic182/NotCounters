using NotCounters.Core.Config;
using UnityEngine;

namespace NotCounters.UI
{
    internal static class ColorConversion
    {
        public static Color ToUnity(ColorValue value) => Hud.HudMath.ToUnity(value);

        public static ColorValue WithAlphaOf(Color picked, ColorValue stored) => new ColorValue(picked.r, picked.g, picked.b, stored.A);
    }
}
