using System;
using NotCounters.Core.Config;
using UnityEngine;

namespace NotCounters.Hud
{
    internal static class HudMath
    {
        internal const int UiLayer = 5;

        internal const float GhostAlpha = 0.25f;

        internal static Color ToUnity(this ColorValue color) => new Color(color.R, color.G, color.B, color.A);

        internal static Vector3 ToUnity(this Vec3 value) => new Vector3(value.X, value.Y, value.Z);

        internal static Vec3 ToConfigPosition(Vector3 value) =>
            new Vec3(Round(value.x, 4), Round(value.y, 4), Round(value.z, 4));

        internal static Vec3 ToConfigEuler(Quaternion rotation)
        {
            var euler = rotation.eulerAngles;
            return new Vec3(
                Round(NormalizeAngle(euler.x), 3),
                Round(NormalizeAngle(euler.y), 3),
                Round(NormalizeAngle(euler.z), 3));
        }

        internal static float ToConfigScale(float scale) => Round(scale, 4);

        internal static Color Tint(ColorValue configured, float originalAlpha) =>
            new Color(configured.R, configured.G, configured.B, configured.A * originalAlpha);

        internal static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees <= -180f)
            {
                degrees += 360f;
            }

            return degrees;
        }

        internal static float UniformScale(Vector3 localScale) => Math.Abs(localScale.x) < 1e-6f ? 1f : localScale.x;

        private static float Round(float value, int decimals) => (float)Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }
}
