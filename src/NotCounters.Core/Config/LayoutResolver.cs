using System;
using System.Collections.Generic;

namespace NotCounters.Core.Config
{
    public static class LayoutResolver
    {
        public static HudLayout GetEffective(HudProfile profile, string? environment)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            return TryGetOverride(profile, environment) ?? BaseLayout(profile);
        }

        public static HudLayout GetEditable(HudProfile profile, string? environment, bool environmentSpecific)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            if (!environmentSpecific || string.IsNullOrWhiteSpace(environment))
            {
                return BaseLayout(profile);
            }

            HudLayout? existing = TryGetOverride(profile, environment);
            if (existing != null)
            {
                return existing;
            }

            HudLayout created = ConfigSerializer.DeepClone(BaseLayout(profile));
            Overrides(profile)[environment!] = created;
            return created;
        }

        public static bool RemoveOverride(HudProfile profile, string environment)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            return environment != null && profile.EnvironmentOverrides != null && profile.EnvironmentOverrides.Remove(environment);
        }

        private static HudLayout? TryGetOverride(HudProfile profile, string? environment)
        {
            if (string.IsNullOrWhiteSpace(environment) || profile.EnvironmentOverrides == null)
            {
                return null;
            }

            return profile.EnvironmentOverrides.TryGetValue(environment!, out HudLayout? layout) ? layout : null;
        }

        private static HudLayout BaseLayout(HudProfile profile) =>
            profile.Layout ??= DefaultConfigFactory.CreateDefaultLayout();

        private static Dictionary<string, HudLayout> Overrides(HudProfile profile) =>
            profile.EnvironmentOverrides ??= new Dictionary<string, HudLayout>(StringComparer.Ordinal);
    }
}
