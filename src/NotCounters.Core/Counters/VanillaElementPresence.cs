using System;
using System.Collections.Generic;
using NotCounters.Core.Config;

namespace NotCounters.Core.Counters
{
    public enum ElementActiveTarget
    {
        Vanilla,
        Active,
        Inactive,
    }

    public static class VanillaElementPresence
    {
        public static bool IsRemoved(HudLayout? layout, string? id) =>
            layout?.Vanilla != null
            && id != null
            && layout.Vanilla.TryGetValue(id, out VanillaElementSettings? settings)
            && settings != null
            && settings.Removed;

        public static IReadOnlyList<string> ListedIds(HudLayout? layout) => Select(layout, removed: false);

        public static IReadOnlyList<string> RemovedIds(HudLayout? layout) => Select(layout, removed: true);

        public static VanillaElementSettings Remove(HudLayout layout, string id)
        {
            VanillaElementSettings settings = GetOrCreate(layout, id);
            settings.Removed = true;
            return settings;
        }

        public static VanillaElementSettings Restore(HudLayout layout, string id)
        {
            VanillaElementSettings settings = GetOrCreate(layout, id);
            settings.Removed = false;
            settings.Visible = true;
            return settings;
        }

        public static VanillaElementSettings ResetSettings(HudLayout layout, string id)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (id == null) throw new ArgumentNullException(nameof(id));

            layout.Vanilla ??= new Dictionary<string, VanillaElementSettings>(StringComparer.Ordinal);
            VanillaElementSettings fresh = DefaultConfigFactory.CreateVanillaDefaults(id);
            if (layout.Vanilla.TryGetValue(id, out VanillaElementSettings? old) && old != null)
            {
                fresh.Visible = old.Visible;
                fresh.Removed = old.Removed;
            }

            layout.Vanilla[id] = fresh;
            return fresh;
        }

        public static ElementActiveTarget Target(string? id, bool removed, bool visible, bool forceShow, bool isEditSession, bool hudShown)
        {
            if (removed)
            {
                return ElementActiveTarget.Inactive;
            }

            return hudShown && AdvancedHudElements.ShouldActivate(id, visible, forceShow, isEditSession)
                ? ElementActiveTarget.Active
                : ElementActiveTarget.Vanilla;
        }

        private static IReadOnlyList<string> Select(HudLayout? layout, bool removed)
        {
            var ids = new List<string>(VanillaElementCatalog.All.Count);
            foreach (VanillaElementDescriptor descriptor in VanillaElementCatalog.All)
            {
                if (IsRemoved(layout, descriptor.Id) == removed)
                {
                    ids.Add(descriptor.Id);
                }
            }

            return ids.AsReadOnly();
        }

        private static VanillaElementSettings GetOrCreate(HudLayout layout, string id)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (id == null) throw new ArgumentNullException(nameof(id));

            layout.Vanilla ??= new Dictionary<string, VanillaElementSettings>(StringComparer.Ordinal);
            if (!layout.Vanilla.TryGetValue(id, out VanillaElementSettings? settings) || settings == null)
            {
                settings = DefaultConfigFactory.CreateVanillaDefaults(id);
                layout.Vanilla[id] = settings;
            }

            return settings;
        }
    }
}
