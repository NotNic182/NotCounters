using System;
using System.Collections.Generic;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;

namespace NotCounters.UI
{
    internal static class LayoutEditing
    {
        private static readonly Vec3 NewCounterBasePosition = new Vec3(0f, 2.4f, 7f);

        private const float NewCounterSpacing = DefaultConfigFactory.RowPitchMetres;

        private const float OccupiedRadius = 0.2f;

        private const float FitToleranceMetres = 0.01f;

        private const int MaxPlacementAttempts = 8;

        public static CounterSettings? FindCounter(HudLayout layout, string counterId)
        {
            var counters = layout.Counters;
            if (counters == null)
            {
                return null;
            }

            foreach (var counter in counters)
            {
                if (counter != null && string.Equals(counter.Id, counterId, StringComparison.Ordinal))
                {
                    return counter;
                }
            }

            return null;
        }

        public static int IndexOfCounter(HudLayout layout, string counterId)
        {
            var counters = layout.Counters;
            if (counters == null)
            {
                return -1;
            }

            for (var i = 0; i < counters.Count; i++)
            {
                if (counters[i] != null && string.Equals(counters[i].Id, counterId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public static VanillaElementSettings GetVanilla(HudLayout layout, string elementId)
        {
            layout.Vanilla ??= new Dictionary<string, VanillaElementSettings>(StringComparer.Ordinal);
            if (!layout.Vanilla.TryGetValue(elementId, out var settings) || settings == null)
            {
                settings = DefaultConfigFactory.CreateVanillaDefaults(elementId);
                layout.Vanilla[elementId] = settings;
            }

            return settings;
        }

        public static void ResetVanilla(HudLayout layout, string elementId) =>
            VanillaElementPresence.ResetSettings(layout, elementId);

        public static CounterSettings AddCounter(HudLayout layout, string typeId)
        {
            layout.Counters ??= new List<CounterSettings>();
            var counter = DefaultConfigFactory.CreateCounter(typeId, FindFreePosition(layout, RowsTall(typeId)), Vec3.Zero);
            CounterOptions.ApplyDefaults(counter);
            layout.Counters.Add(counter);
            return counter;
        }

        public static CounterSettings Duplicate(HudLayout layout, CounterSettings source)
        {
            layout.Counters ??= new List<CounterSettings>();
            var copy = Core.Config.ConfigSerializer.DeepClone(source);
            copy.Id = Guid.NewGuid().ToString("N");
            var offsetY = -RowsTall(source.Type) * NewCounterSpacing;
            copy.Position = new Vec3(source.Position.X, (float)Math.Round(source.Position.Y + offsetY, 3), source.Position.Z);
            if (!string.IsNullOrEmpty(copy.Name))
            {
                copy.Name += " copy";
            }

            var index = IndexOfCounter(layout, source.Id);
            if (index < 0)
            {
                layout.Counters.Add(copy);
            }
            else
            {
                layout.Counters.Insert(index + 1, copy);
            }

            return copy;
        }

        public static int Remove(HudLayout layout, string counterId)
        {
            var index = IndexOfCounter(layout, counterId);
            if (index >= 0)
            {
                layout.Counters.RemoveAt(index);
            }

            return index;
        }

        public static void ResetCounter(HudLayout layout, CounterSettings counter)
        {
            var fresh = DefaultConfigFactory.CreateCounter(counter.Type, counter.Position, Vec3.Zero);
            CounterOptions.ApplyDefaults(fresh);
            fresh.Id = counter.Id;
            fresh.Enabled = counter.Enabled;
            fresh.Attachment = counter.Attachment;

            var index = IndexOfCounter(layout, counter.Id);
            if (index >= 0)
            {
                layout.Counters[index] = fresh;
            }
        }

        public static string DisplayName(CounterSettings counter)
        {
            if (!string.IsNullOrWhiteSpace(counter.Name))
            {
                return counter.Name!.Trim();
            }

            return TypeDisplayName(counter.Type);
        }

        public static string TypeDisplayName(string typeId)
        {
            var descriptor = CounterCatalog.GetDescriptor(typeId);
            if (descriptor != null)
            {
                return descriptor.DisplayName;
            }

            return string.IsNullOrEmpty(typeId) ? "Missing type" : typeId;
        }

        public static HudProfile CreateProfile(HudConfig config, string baseName)
        {
            var profile = DefaultConfigFactory.CreateProfile(Guid.NewGuid().ToString("N"), ProfileNames.Unique(config.Profiles, baseName));
            config.Profiles.Add(profile);
            return profile;
        }

        public static HudProfile DuplicateProfile(HudConfig config, HudProfile source)
        {
            var copy = new HudProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = ProfileNames.Unique(config.Profiles, source.Name, " copy"),
                Layout = Core.Config.ConfigSerializer.DeepClone(source.Layout ?? DefaultConfigFactory.CreateDefaultLayout()),
                EnvironmentOverrides = new Dictionary<string, HudLayout>(StringComparer.Ordinal),
            };

            if (source.EnvironmentOverrides != null)
            {
                foreach (var entry in source.EnvironmentOverrides)
                {
                    if (entry.Value != null)
                    {
                        copy.EnvironmentOverrides[entry.Key] = Core.Config.ConfigSerializer.DeepClone(entry.Value);
                    }
                }
            }

            config.Profiles.Add(copy);
            return copy;
        }

        public static void ResetProfile(HudProfile profile)
        {
            profile.Layout = DefaultConfigFactory.CreateDefaultLayout();
            profile.EnvironmentOverrides ??= new Dictionary<string, HudLayout>(StringComparer.Ordinal);
            profile.EnvironmentOverrides.Clear();
        }

        private static int RowsTall(string? typeId) => string.Equals(typeId, CounterTypeIds.Pp, StringComparison.Ordinal) ? 2 : 1;

        private static Vec3 FindFreePosition(HudLayout layout, int rows)
        {
            for (var attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                var candidate = Slot(attempt, rows);
                if (!IsOccupied(layout, candidate, rows))
                {
                    return candidate;
                }
            }

            return Slot(0, rows);
        }

        private static Vec3 Slot(int attempt, int rows) =>
            new Vec3(
                NewCounterBasePosition.X,
                (float)Math.Round(NewCounterBasePosition.Y + ((attempt + ((rows - 1) / 2f)) * NewCounterSpacing), 3),
                NewCounterBasePosition.Z);

        private static bool IsOccupied(HudLayout layout, Vec3 position, int rows)
        {
            var counters = layout.Counters;
            if (counters == null)
            {
                return false;
            }

            foreach (var counter in counters)
            {
                if (counter == null)
                {
                    continue;
                }

                var d = counter.Position - position;
                if (Math.Abs(d.X) >= OccupiedRadius || Math.Abs(d.Z) >= OccupiedRadius)
                {
                    continue;
                }

                var reach = ((rows + RowsTall(counter.Type)) * NewCounterSpacing / 2f) - FitToleranceMetres;
                if (Math.Abs(d.Y) < reach)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
