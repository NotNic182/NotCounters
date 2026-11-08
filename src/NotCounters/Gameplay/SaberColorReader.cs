using NotCounters.Core.Config;
using NotCounters.Core.Stats;
using UnityEngine;

namespace NotCounters.Gameplay
{
    internal static class SaberColorReader
    {
        public static void Fill(StatsSnapshot snapshot, GameplayCoreSceneSetupData? sceneSetupData)
        {
            ColorScheme? scheme = sceneSetupData?.colorScheme;
            if (scheme == null)
            {
                return;
            }

            snapshot.Left.SaberColor = Opaque(scheme.saberAColor);
            snapshot.Right.SaberColor = Opaque(scheme.saberBColor);
        }

        private static ColorValue Opaque(Color color) => new ColorValue(color.r, color.g, color.b, 1f);
    }
}
