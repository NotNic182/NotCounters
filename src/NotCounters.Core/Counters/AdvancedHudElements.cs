using System;
using System.Collections.Generic;
using NotCounters.Core.Config;

namespace NotCounters.Core.Counters
{
    public static class AdvancedHudElements
    {
        private static readonly string[] IdList =
        {
            VanillaElementIds.Rank, VanillaElementIds.RelativeScore, VanillaElementIds.SongProgress,
        };

        private static readonly HashSet<string> IdSet = new HashSet<string>(IdList, StringComparer.Ordinal);

        public static IReadOnlyList<string> Ids => IdList;

        public static bool Contains(string? elementId) => elementId != null && IdSet.Contains(elementId);

        public static bool IsHiddenInPlay(string? elementId, bool playerAdvancedHud, bool forceShow) =>
            Contains(elementId) && !playerAdvancedHud && !forceShow;

        public static bool ShouldActivate(string? elementId, bool visible, bool forceShow, bool isEditSession) =>
            Contains(elementId) && (isEditSession || (forceShow && visible));

        public static bool AnyOnHud(HudLayout? layout)
        {
            foreach (string id in IdList)
            {
                VanillaElementSettings? settings = null;
                if (layout?.Vanilla == null || !layout.Vanilla.TryGetValue(id, out settings) || settings == null
                    || (!settings.Removed && settings.Visible))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
