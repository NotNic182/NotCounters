using System;
using System.Collections.Generic;
using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters
{
    public static class LeaderboardDemand
    {
        private static readonly IReadOnlyList<PbSource> NoSources = new PbSource[0];

        public static bool UsesLeaderboardData(CounterSettings? counter)
        {
            if (counter == null || !counter.Enabled) return false;
            switch (counter.Type)
            {
                case CounterTypeIds.Pp:
                    return true;
                case CounterTypeIds.MapInfo:
                    return CounterOptions.GetBool(counter, CounterOptionKeys.ShowBeatLeaderStars)
                           || CounterOptions.GetBool(counter, CounterOptionKeys.ShowScoreSaberStars);
                case CounterTypeIds.PersonalBest:
                    foreach (PbSource source in PersonalBestSources(counter))
                    {
                        if (source != PbSource.Local) return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        public static bool Needs(HudLayout? layout)
        {
            if (layout?.Counters == null) return false;
            foreach (CounterSettings counter in layout.Counters)
            {
                if (UsesLeaderboardData(counter)) return true;
            }

            return false;
        }

        public static bool Needs(HudProfile? profile)
        {
            if (profile == null) return false;
            if (Needs(profile.Layout)) return true;
            if (profile.EnvironmentOverrides == null) return false;
            foreach (KeyValuePair<string, HudLayout> environment in profile.EnvironmentOverrides)
            {
                if (Needs(environment.Value)) return true;
            }

            return false;
        }

        public static IReadOnlyList<PbSource> RemotePersonalBestSources(HudLayout? layout)
        {
            var sources = new List<PbSource>(2);
            if (layout?.Counters == null) return sources;
            foreach (CounterSettings counter in layout.Counters)
            {
                if (counter == null || !counter.Enabled) continue;
                foreach (PbSource source in PersonalBestSources(counter))
                {
                    if (source != PbSource.Local && !sources.Contains(source)) sources.Add(source);
                }
            }

            return sources;
        }

        private static IReadOnlyList<PbSource> PersonalBestSources(CounterSettings counter) =>
            string.Equals(counter.Type, CounterTypeIds.PersonalBest, StringComparison.Ordinal)
                ? CounterChoices.PbSourceCandidates(CounterOptions.GetChoice(counter, CounterOptionKeys.Source))
                : NoSources;
    }
}
