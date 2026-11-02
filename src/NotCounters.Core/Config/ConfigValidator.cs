using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NotCounters.Core.Counters;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Config
{
    public static class ConfigValidator
    {
        public const float MinScale = 0.05f;

        public const float MaxScale = 20f;

        public const float MaxPositionMetres = 50f;

        public const float MinFontSize = 0.5f;

        public const float MaxFontSize = 100f;

        public const float MinFontSizeMultiplier = 0.25f;

        public const float MaxFontSizeMultiplier = 4f;

        public const int MinDecimals = 0;

        public const int MaxDecimals = 6;

        public const float MinLabelScale = 0.2f;

        public const float MaxLabelScale = 2f;

        public const float MinOutlineWidth = 0f;

        public const float MaxOutlineWidth = 1f;

        public const float MinGridSize = 0.01f;

        public const float MaxGridSize = 0.5f;

        public const float MinAngleSnapDegrees = 1f;

        public const float MaxAngleSnapDegrees = 90f;

        public const float MinThumbstickDeadzone = 0f;

        public const float MaxThumbstickDeadzone = 0.95f;

        public const float MinPushPullSpeed = 0.1f;

        public const float MaxPushPullSpeed = 20f;

        public const float MinScaleSpeed = 0.05f;

        public const float MaxScaleSpeed = 10f;

        public const float MinPreviewStartFraction = 0f;

        public const float MaxPreviewStartFraction = 0.9f;

        public const double MinLeaderboardCacheHours = 0;

        public const double MaxLeaderboardCacheHours = 24;

        internal const long FirstSchemaWithoutReleaseDefaults = 3;

        internal const long FirstSchemaFollowingTheHud = 4;

        private static readonly GlobalSettings GlobalDefaults = new GlobalSettings();
        private static readonly EditorSettings EditorDefaults = new EditorSettings();
        private static readonly TextStyle StyleDefaults = new TextStyle();
        private static readonly CounterSettings CounterDefaults = new CounterSettings();
        private static readonly VanillaElementSettings VanillaDefaults = new VanillaElementSettings();

        private static readonly HashSet<string> KnownVanillaIds = new HashSet<string>(VanillaElementIds.All, StringComparer.Ordinal);

        public static IReadOnlyList<string> Normalize(HudConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var warnings = new List<string>();
            NormalizeGlobal(config, warnings);
            NormalizeProfiles(config, warnings);
            return warnings.AsReadOnly();
        }

        private static void NormalizeGlobal(HudConfig config, List<string> warnings)
        {
            if (config.Global == null)
            {
                config.Global = new GlobalSettings();
                warnings.Add("global: missing; defaults used.");
            }

            GlobalSettings global = config.Global;
            if (string.IsNullOrWhiteSpace(global.PreviewEnvironment))
            {
                global.PreviewEnvironment = GlobalDefaults.PreviewEnvironment;
                warnings.Add($"global.previewEnvironment: empty; reset to {global.PreviewEnvironment}.");
            }

            global.PreviewLevelId = NullIfBlank(global.PreviewLevelId, "global.previewLevelId", warnings);
            global.BeatLeaderPlayerId = NullIfBlank(global.BeatLeaderPlayerId, "global.beatLeaderPlayerId", warnings);
            global.ScoreSaberPlayerId = NullIfBlank(global.ScoreSaberPlayerId, "global.scoreSaberPlayerId", warnings);
            global.BeatLeaderApiBaseUrl = HttpUrlOrDefault(global.BeatLeaderApiBaseUrl, GlobalDefaults.BeatLeaderApiBaseUrl, "global.beatLeaderApiBaseUrl", warnings);
            global.ScoreSaberApiBaseUrl = HttpUrlOrDefault(global.ScoreSaberApiBaseUrl, GlobalDefaults.ScoreSaberApiBaseUrl, "global.scoreSaberApiBaseUrl", warnings);
            global.LeaderboardCacheHours = Clamp(global.LeaderboardCacheHours, MinLeaderboardCacheHours, MaxLeaderboardCacheHours, GlobalDefaults.LeaderboardCacheHours, "global.leaderboardCacheHours", warnings);

            if (global.Editor == null)
            {
                global.Editor = new EditorSettings();
                warnings.Add("global.editor: missing; defaults used.");
            }

            EditorSettings editor = global.Editor;
            editor.GridSize = Clamp(editor.GridSize, MinGridSize, MaxGridSize, EditorDefaults.GridSize, "global.editor.gridSize", warnings);
            editor.AngleSnapDegrees = Clamp(editor.AngleSnapDegrees, MinAngleSnapDegrees, MaxAngleSnapDegrees, EditorDefaults.AngleSnapDegrees, "global.editor.angleSnapDegrees", warnings);
            editor.ThumbstickDeadzone = Clamp(editor.ThumbstickDeadzone, MinThumbstickDeadzone, MaxThumbstickDeadzone, EditorDefaults.ThumbstickDeadzone, "global.editor.thumbstickDeadzone", warnings);
            editor.PushPullSpeed = Clamp(editor.PushPullSpeed, MinPushPullSpeed, MaxPushPullSpeed, EditorDefaults.PushPullSpeed, "global.editor.pushPullSpeed", warnings);
            editor.ScaleSpeed = Clamp(editor.ScaleSpeed, MinScaleSpeed, MaxScaleSpeed, EditorDefaults.ScaleSpeed, "global.editor.scaleSpeed", warnings);
            editor.PreviewStartFraction = Clamp(editor.PreviewStartFraction, MinPreviewStartFraction, MaxPreviewStartFraction, EditorDefaults.PreviewStartFraction, "global.editor.previewStartFraction", warnings);
        }

        private static void NormalizeProfiles(HudConfig config, List<string> warnings)
        {
            if (config.Profiles == null)
            {
                config.Profiles = new List<HudProfile>();
                warnings.Add("profiles: missing; replaced by an empty list.");
            }

            int nullProfiles = config.Profiles.RemoveAll(profile => profile == null);
            if (nullProfiles > 0)
            {
                warnings.Add($"profiles: removed {nullProfiles} empty entr{(nullProfiles == 1 ? "y" : "ies")}.");
            }

            var profileIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                HudProfile profile = config.Profiles[i];
                string path = $"profiles[{i}]";
                profile.Id = UniqueId(profile.Id, profileIds, path, warnings);

                if (profile.Layout == null)
                {
                    profile.Layout = DefaultConfigFactory.CreateDefaultLayout();
                    warnings.Add($"{path}.layout: missing; the default layout was used.");
                }
                else
                {
                    NormalizeLayout(profile.Layout, path + ".layout", warnings);
                }

                NormalizeOverrides(profile, path, warnings);
            }

            if (config.Profiles.Count == 0)
            {
                config.Profiles.Add(DefaultConfigFactory.CreateProfile(HudProfile.DefaultProfileId, "Default"));
                warnings.Add("profiles: none defined; the default profile was added.");
            }

            ProfileNames.Repair(config.Profiles, warnings);

            if (config.ActiveProfileId == null || !config.Profiles.Any(profile => profile.Id == config.ActiveProfileId))
            {
                string? previous = config.ActiveProfileId;
                config.ActiveProfileId = config.Profiles[0].Id;
                warnings.Add($"activeProfileId: \"{previous}\" does not name a profile; \"{config.ActiveProfileId}\" is active instead.");
            }
        }

        private static void NormalizeOverrides(HudProfile profile, string profilePath, List<string> warnings)
        {
            if (profile.EnvironmentOverrides == null)
            {
                profile.EnvironmentOverrides = new Dictionary<string, HudLayout>(StringComparer.Ordinal);
                warnings.Add($"{profilePath}.environmentOverrides: missing; replaced by an empty set.");
                return;
            }

            foreach (string environment in profile.EnvironmentOverrides.Keys.ToList())
            {
                string path = $"{profilePath}.environmentOverrides[\"{environment}\"]";
                HudLayout layout = profile.EnvironmentOverrides[environment];
                if (string.IsNullOrWhiteSpace(environment) || layout == null)
                {
                    profile.EnvironmentOverrides.Remove(environment);
                    warnings.Add($"{path}: blank environment name or empty layout; override removed.");
                    continue;
                }

                NormalizeLayout(layout, path, warnings);
            }
        }

        private static void NormalizeLayout(HudLayout layout, string layoutPath, List<string> warnings)
        {
            NormalizeVanillaSet(layout, layoutPath, warnings);

            if (layout.Counters == null)
            {
                layout.Counters = new List<CounterSettings>();
                warnings.Add($"{layoutPath}.counters: missing; replaced by an empty list.");
            }

            int nullCounters = layout.Counters.RemoveAll(counter => counter == null);
            if (nullCounters > 0)
            {
                warnings.Add($"{layoutPath}.counters: removed {nullCounters} empty entr{(nullCounters == 1 ? "y" : "ies")}.");
            }

            LegacyPpCounters.Convert(layout.Counters, layoutPath, warnings);

            var counterIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layout.Counters.Count; i++)
            {
                NormalizeCounter(layout.Counters[i], $"{layoutPath}.counters[{i}]", counterIds, warnings);
            }
        }

        private static void NormalizeVanillaSet(HudLayout layout, string layoutPath, List<string> warnings)
        {
            if (layout.Vanilla == null)
            {
                layout.Vanilla = new Dictionary<string, VanillaElementSettings>(StringComparer.Ordinal);
                warnings.Add($"{layoutPath}.vanilla: missing; replaced by defaults.");
            }

            foreach (string key in layout.Vanilla.Keys.ToList())
            {
                if (layout.Vanilla[key] != null)
                {
                    continue;
                }

                if (KnownVanillaIds.Contains(key))
                {
                    layout.Vanilla[key] = DefaultConfigFactory.CreateVanillaDefaults(key);
                    warnings.Add($"{layoutPath}.vanilla[\"{key}\"]: empty; defaults used.");
                }
                else
                {
                    layout.Vanilla.Remove(key);
                    warnings.Add($"{layoutPath}.vanilla[\"{key}\"]: empty entry for an unknown element removed.");
                }
            }

            foreach (string elementId in VanillaElementIds.All)
            {
                if (!layout.Vanilla.ContainsKey(elementId))
                {
                    layout.Vanilla[elementId] = DefaultConfigFactory.CreateVanillaDefaults(elementId);
                    warnings.Add($"{layoutPath}.vanilla[\"{elementId}\"]: missing; defaults added.");
                }
            }

            foreach (KeyValuePair<string, VanillaElementSettings> entry in layout.Vanilla)
            {
                NormalizeVanilla(entry.Value, $"{layoutPath}.vanilla[\"{entry.Key}\"]", warnings);
            }
        }

        private static void NormalizeVanilla(VanillaElementSettings element, string path, List<string> warnings)
        {
            element.PositionOffset = ClampPosition(element.PositionOffset, path + ".positionOffset", warnings);
            element.RotationOffset = WrapRotation(element.RotationOffset, path + ".rotationOffset", warnings);
            element.Scale = Clamp(element.Scale, MinScale, MaxScale, VanillaDefaults.Scale, path + ".scale", warnings);
            element.FontSizeMultiplier = Clamp(element.FontSizeMultiplier, MinFontSizeMultiplier, MaxFontSizeMultiplier, VanillaDefaults.FontSizeMultiplier, path + ".fontSizeMultiplier", warnings);
            if (element.Decimals.HasValue)
            {
                element.Decimals = Clamp(element.Decimals.Value, MinDecimals, MaxDecimals, path + ".decimals", warnings);
            }

            element.NumberGrouping = CanonicalVanillaGrouping(Defined(element.NumberGrouping, VanillaDefaults.NumberGrouping, path + ".numberGrouping", warnings));
            element.TimeMode = CanonicalVanillaTimeMode(Defined(element.TimeMode, VanillaDefaults.TimeMode, path + ".timeMode", warnings));
            element.OutlineWidth = Clamp(element.OutlineWidth, MinOutlineWidth, MaxOutlineWidth, VanillaDefaults.OutlineWidth, path + ".outlineWidth", warnings);
        }

        private static void NormalizeCounter(CounterSettings counter, string path, HashSet<string> layoutIds, List<string> warnings)
        {
            counter.Id = UniqueId(counter.Id, layoutIds, path, warnings);

            if (counter.Type == null)
            {
                counter.Type = string.Empty;
                warnings.Add($"{path}.type: missing; the counter is kept but not shown.");
            }

            EnsureStyleAndOptions(counter, path, warnings);

            if (CounterCatalog.Get(counter.Type) == null)
            {
                return;
            }

            counter.Attachment = Defined(counter.Attachment, CounterDefaults.Attachment, path + ".attachment", warnings);
            if (counter.Attachment == CounterAttachment.LeftPanel || counter.Attachment == CounterAttachment.RightPanel)
            {
                warnings.Add($"{path}.attachment: {counter.Attachment} now follows the whole HUD ({CounterAttachment.Hud}).");
                counter.Attachment = CounterAttachment.Hud;
            }
            counter.Position = ClampPosition(counter.Position, path + ".position", warnings);
            counter.Rotation = WrapRotation(counter.Rotation, path + ".rotation", warnings);
            counter.Scale = Clamp(counter.Scale, MinScale, MaxScale, CounterDefaults.Scale, path + ".scale", warnings);

            TextStyle style = counter.Style;
            string stylePath = path + ".style";
            style.FontSize = Clamp(style.FontSize, MinFontSize, MaxFontSize, StyleDefaults.FontSize, stylePath + ".fontSize", warnings);
            style.LabelScale = Clamp(style.LabelScale, MinLabelScale, MaxLabelScale, StyleDefaults.LabelScale, stylePath + ".labelScale", warnings);
            style.Decimals = Clamp(style.Decimals, MinDecimals, MaxDecimals, stylePath + ".decimals", warnings);
            style.OutlineWidth = Clamp(style.OutlineWidth, MinOutlineWidth, MaxOutlineWidth, StyleDefaults.OutlineWidth, stylePath + ".outlineWidth", warnings);
            style.LabelPlacement = Defined(style.LabelPlacement, StyleDefaults.LabelPlacement, stylePath + ".labelPlacement", warnings);
            style.Alignment = Defined(style.Alignment, StyleDefaults.Alignment, stylePath + ".alignment", warnings);
        }

        internal static void EnsureStyleAndOptions(CounterSettings counter, string path, List<string> warnings)
        {
            if (counter.Style == null)
            {
                counter.Style = new TextStyle();
                warnings.Add($"{path}.style: missing; defaults used.");
            }

            if (counter.Options == null)
            {
                counter.Options = new Dictionary<string, JToken>(StringComparer.Ordinal);
                warnings.Add($"{path}.options: missing; replaced by an empty set.");
            }
        }

        internal static void UpgradeReleaseDefaults(HudConfig config, long fileVersion, List<string> warnings)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));

            if (fileVersion >= FirstSchemaFollowingTheHud)
            {
                return;
            }

            if (fileVersion < FirstSchemaWithoutReleaseDefaults)
            {
                ForEachCounter(config, (counter, path, _, list) => UpgradeCounterReleaseDefaults(counter, path, list), warnings);
            }

            ForEachCounter(config, UpgradeCounterFourthRelease, warnings);
            if (config.Global != null && config.Global.UniformHudText)
            {
                config.Global.UniformHudText = false;
                warnings.Add("global.uniformHudText: turned off; the HUD uses the game's own text look again.");
            }
        }

        private static void ForEachCounter(HudConfig config, Action<CounterSettings?, string, bool, List<string>> upgrade, List<string> warnings)
        {
            if (config.Profiles == null)
            {
                return;
            }

            for (int i = 0; i < config.Profiles.Count; i++)
            {
                HudProfile profile = config.Profiles[i];
                if (profile == null)
                {
                    continue;
                }

                string path = $"profiles[{i}]";
                UpgradeLayoutCounters(profile.Layout, path + ".layout", false, upgrade, warnings);
                if (profile.EnvironmentOverrides == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, HudLayout> entry in profile.EnvironmentOverrides)
                {
                    UpgradeLayoutCounters(entry.Value, $"{path}.environmentOverrides[\"{entry.Key}\"]", true, upgrade, warnings);
                }
            }
        }

        private static void UpgradeLayoutCounters(HudLayout? layout, string layoutPath, bool environmentLayout, Action<CounterSettings?, string, bool, List<string>> upgrade, List<string> warnings)
        {
            if (layout?.Counters == null)
            {
                return;
            }

            for (int i = 0; i < layout.Counters.Count; i++)
            {
                upgrade(layout.Counters[i], $"{layoutPath}.counters[{i}]", environmentLayout, warnings);
            }
        }

        private static void UpgradeCounterFourthRelease(CounterSettings? counter, string path, bool environmentLayout, List<string> warnings)
        {
            if (counter?.Type == null)
            {
                return;
            }

            bool keepsStayingPut = environmentLayout && counter.Attachment == CounterAttachment.None;

            if (LegacyCounterTypeIds.IsLegacy(counter.Type))
            {
                if (counter.Attachment == CounterAttachment.None && !keepsStayingPut)
                {
                    warnings.Add($"{path}.attachment: {counter.Attachment} -> {CounterAttachment.Hud} (the counter now follows the environment's HUD).");
                    counter.Attachment = CounterAttachment.Hud;
                }

                return;
            }

            if (CounterCatalog.Get(counter.Type) == null)
            {
                return;
            }

            if (counter.Attachment != CounterAttachment.Hud && !keepsStayingPut)
            {
                warnings.Add($"{path}.attachment: {counter.Attachment} -> {CounterAttachment.Hud} (the counter now follows the environment's HUD).");
                counter.Attachment = CounterAttachment.Hud;
            }

            if (DefaultConfigFactory.UpgradeThirdReleaseTextSize(counter))
            {
                warnings.Add($"{path}.style: third-release default text size updated to the game's own (font {Format(TextStyle.DefaultFontSize)}).");
            }
        }

        private static void UpgradeCounterReleaseDefaults(CounterSettings? counter, string path, List<string> warnings)
        {
            if (counter?.Type == null || (CounterCatalog.Get(counter.Type) == null && !LegacyCounterTypeIds.IsLegacy(counter.Type)))
            {
                return;
            }

            switch (DefaultConfigFactory.UpgradeFirstReleaseDefaults(counter))
            {
                case FirstReleaseUpgrade.Restyled:
                    warnings.Add($"{path}.style: first-release default colours updated to the vanilla HUD look.");
                    break;
                case FirstReleaseUpgrade.RestyledAndMoved:
                    warnings.Add($"{path}: first-release default counter updated to the vanilla HUD look, size and default place.");
                    return;
            }

            if (DefaultConfigFactory.UpgradeFirstReleaseTextSize(counter))
            {
                float labelScale = counter.Style.LabelScale;
                warnings.Add(labelScale == TextStyle.DefaultLabelScale
                    ? $"{path}: first-release text size updated to the current default (font {Format(TextStyle.DefaultFontSize)}, caption {Format(labelScale)}x)."
                    : $"{path}: first-release text size updated to the current default (font {Format(TextStyle.DefaultFontSize)}; the caption keeps its {Format(labelScale)}x).");
            }

            if (DefaultConfigFactory.UpgradeSecondReleaseDefaults(counter))
            {
                warnings.Add($"{path}: second-release default counter updated to the current default size and place.");
            }
        }

        private static string UniqueId(string? id, HashSet<string> seen, string path, List<string> warnings)
        {
            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id!))
            {
                return id!;
            }

            string replacement;
            do
            {
                replacement = Guid.NewGuid().ToString("N");
            }
            while (!seen.Add(replacement));

            warnings.Add(string.IsNullOrWhiteSpace(id)
                ? $"{path}.id: missing; assigned {replacement}."
                : $"{path}.id: \"{id}\" is used twice in the same list; this entry now has id {replacement}.");
            return replacement;
        }

        private static Vec3 ClampPosition(Vec3 value, string path, List<string> warnings) => new Vec3(
            Clamp(value.X, -MaxPositionMetres, MaxPositionMetres, 0f, path + ".x", warnings),
            Clamp(value.Y, -MaxPositionMetres, MaxPositionMetres, 0f, path + ".y", warnings),
            Clamp(value.Z, -MaxPositionMetres, MaxPositionMetres, 0f, path + ".z", warnings));

        private static Vec3 WrapRotation(Vec3 value, string path, List<string> warnings) => new Vec3(
            WrapAngle(value.X, path + ".x", warnings),
            WrapAngle(value.Y, path + ".y", warnings),
            WrapAngle(value.Z, path + ".z", warnings));

        private static float WrapAngle(float degrees, string path, List<string> warnings)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                warnings.Add($"{path}: {Format(degrees)} is not a finite angle; reset to 0.");
                return 0f;
            }

            if (degrees > -360f && degrees <= 360f)
            {
                return degrees;
            }

            float wrapped = (degrees % 360f) + 0f;
            warnings.Add($"{path}: {Format(degrees)} wrapped to {Format(wrapped)}.");
            return wrapped;
        }

        private static float Clamp(float value, float min, float max, float fallback, string path, List<string> warnings)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                warnings.Add($"{path}: {Format(value)} is not a finite number; reset to {Format(fallback)}.");
                return fallback;
            }

            return ClampRange(value, min, max, path, warnings);
        }

        private static double Clamp(double value, double min, double max, double fallback, string path, List<string> warnings)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                warnings.Add($"{path}: {Format(value)} is not a finite number; reset to {Format(fallback)}.");
                return fallback;
            }

            return ClampRange(value, min, max, path, warnings);
        }

        private static int Clamp(int value, int min, int max, string path, List<string> warnings) =>
            ClampRange(value, min, max, path, warnings);

        private static T ClampRange<T>(T value, T min, T max, string path, List<string> warnings)
            where T : struct, IComparable<T>, IFormattable
        {
            if (value.CompareTo(min) < 0)
            {
                warnings.Add($"{path}: {Format(value)} raised to the minimum {Format(min)}.");
                return min;
            }

            if (value.CompareTo(max) > 0)
            {
                warnings.Add($"{path}: {Format(value)} lowered to the maximum {Format(max)}.");
                return max;
            }

            return value;
        }

        private static NumberGrouping CanonicalVanillaGrouping(NumberGrouping grouping) =>
            grouping == NumberGrouping.Space ? NumberGrouping.None : grouping;

        private static TimeDisplayMode CanonicalVanillaTimeMode(TimeDisplayMode mode) =>
            mode == TimeDisplayMode.ElapsedAndTotal ? TimeDisplayMode.Default : mode;

        private static TEnum Defined<TEnum>(TEnum value, TEnum fallback, string path, List<string> warnings)
            where TEnum : struct, Enum
        {
            if (Enum.IsDefined(typeof(TEnum), value))
            {
                return value;
            }

            warnings.Add($"{path}: {Convert.ToInt64(value, CultureInfo.InvariantCulture)} is not a valid {typeof(TEnum).Name}; reset to {fallback}.");
            return fallback;
        }

        private static string? NullIfBlank(string? value, string path, List<string> warnings)
        {
            if (value == null || value.Trim().Length > 0)
            {
                return value;
            }

            warnings.Add($"{path}: blank; cleared.");
            return null;
        }

        private static string HttpUrlOrDefault(string? value, string fallback, string path, List<string> warnings)
        {
            if (value != null
                && Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return value;
            }

            warnings.Add($"{path}: \"{value}\" is not an http(s) address; reset to {fallback}.");
            return fallback;
        }

        private static string Format(IFormattable value) => value.ToString(null, CultureInfo.InvariantCulture);
    }
}
