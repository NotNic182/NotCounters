using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using NotCounters.Core.Counters;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Config
{
    public enum FirstReleaseUpgrade
    {
        None,
        Restyled,
        RestyledAndMoved,
    }

    public static class DefaultConfigFactory
    {
        public const float ColumnOffsetMetres = 3.2f;

        public const float ColumnDepthMetres = 7f;

        public const float LeftFirstRowHeightMetres = -0.22f;

        public const float RightFirstRowHeightMetres = 0.13f;

        public const float RowPitchMetres = 0.79f;

        public const float PpLeftEdgeMetres = 2.3f;

        public const float PpHeightMetres = -1.62f;

        private const string DefaultProfileName = "Default";

        private const float SlotTolerance = 0.001f;

        private const float TextSizeTolerance = 0.001f;

        private const float FirstReleaseFontSize = 12f;
        private const float FirstReleaseLabelScale = 0.6f;
        private const float FirstReleaseFirstRowHeightMetres = -0.35f;
        private const float FirstReleaseRowPitchMetres = 0.5f;
        private static readonly ColorValue FirstReleaseLabelColor = new ColorValue(1f, 1f, 1f, 0.6f);

        private const float ThirdReleaseFontSize = 22f;
        private const float ThirdReleaseLabelScale = 0.75f;

        private const float SecondReleaseFontSize = 20f;
        private const float SecondReleaseLabelScale = 0.75f;
        private const float SecondReleaseLeftFirstRowMetres = -0.25f;
        private const float SecondReleaseRightFirstRowMetres = 0.15f;
        private const float SecondReleaseRowPitchMetres = 0.8f;
        private const float SecondReleasePairHalfGapMetres = 0.2f;

        private static readonly ReadOnlyCollection<string> StarterTypes = Array.AsReadOnly(new[]
        {
            CounterTypeIds.Accuracy,
            CounterTypeIds.MissedNotes,
            CounterTypeIds.FcAccuracy,
            CounterTypeIds.NotesLeft,
            CounterTypeIds.PersonalBest,
            CounterTypeIds.Pp,
        });

        private static readonly Dictionary<string, Slot> DefaultSlots = new Dictionary<string, Slot>(StringComparer.Ordinal)
        {
            [CounterTypeIds.Accuracy] = ColumnSlot(-1, LeftFirstRowHeightMetres - (0 * RowPitchMetres)),
            [CounterTypeIds.FcAccuracy] = ColumnSlot(-1, LeftFirstRowHeightMetres - (1 * RowPitchMetres)),
            [CounterTypeIds.MissedNotes] = ColumnSlot(-1, LeftFirstRowHeightMetres - (2 * RowPitchMetres)),
            [CounterTypeIds.NotesLeft] = ColumnSlot(1, RightFirstRowHeightMetres - (0 * RowPitchMetres)),
            [CounterTypeIds.PersonalBest] = ColumnSlot(1, RightFirstRowHeightMetres - (1 * RowPitchMetres)),
            [CounterTypeIds.Pp] = new Slot(PpLeftEdgeMetres, PpHeightMetres, TextAlign.Left, CounterAttachment.Hud),
        };

        private static readonly Dictionary<string, (int Column, int Row)> FirstReleaseSlots = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            [CounterTypeIds.Accuracy] = (-1, 0),
            [CounterTypeIds.FcAccuracy] = (-1, 1),
            [CounterTypeIds.MissedNotes] = (-1, 2),
            [CounterTypeIds.NotesLeft] = (-1, 3),
            [CounterTypeIds.PersonalBest] = (1, 0),
            [LegacyCounterTypeIds.BeatLeaderPp] = (1, 1),
            [LegacyCounterTypeIds.ScoreSaberPp] = (1, 2),
        };

        private static readonly Dictionary<string, Slot> SecondReleaseSlots = new Dictionary<string, Slot>(StringComparer.Ordinal)
        {
            [CounterTypeIds.Accuracy] = SecondReleaseColumnSlot(-1, SecondReleaseLeftFirstRowMetres - (0 * SecondReleaseRowPitchMetres)),
            [CounterTypeIds.FcAccuracy] = SecondReleaseColumnSlot(-1, SecondReleaseLeftFirstRowMetres - (1 * SecondReleaseRowPitchMetres)),
            [CounterTypeIds.MissedNotes] = SecondReleaseColumnSlot(-1, SecondReleaseLeftFirstRowMetres - (2 * SecondReleaseRowPitchMetres)),
            [CounterTypeIds.NotesLeft] = SecondReleaseColumnSlot(1, SecondReleaseRightFirstRowMetres - (0 * SecondReleaseRowPitchMetres)),
            [CounterTypeIds.PersonalBest] = SecondReleaseColumnSlot(1, SecondReleaseRightFirstRowMetres - (1 * SecondReleaseRowPitchMetres)),
            [LegacyCounterTypeIds.BeatLeaderPp] = new Slot(
                ColumnOffsetMetres - SecondReleasePairHalfGapMetres,
                SecondReleaseRightFirstRowMetres - (2 * SecondReleaseRowPitchMetres),
                TextAlign.Right,
                CounterAttachment.RightPanel),
            [LegacyCounterTypeIds.ScoreSaberPp] = new Slot(
                ColumnOffsetMetres + SecondReleasePairHalfGapMetres,
                SecondReleaseRightFirstRowMetres - (2 * SecondReleaseRowPitchMetres),
                TextAlign.Left,
                CounterAttachment.RightPanel),
        };

        public static IReadOnlyList<string> StarterCounterTypes => StarterTypes;

        public static HudConfig CreateDefault() => new HudConfig
        {
            SchemaVersion = HudConfig.CurrentSchemaVersion,
            Global = new GlobalSettings(),
            ActiveProfileId = HudProfile.DefaultProfileId,
            Profiles = new List<HudProfile> { CreateProfile(HudProfile.DefaultProfileId, DefaultProfileName) },
        };

        public static HudProfile CreateProfile(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A profile id is required.", nameof(id));

            return new HudProfile
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name,
                Layout = CreateDefaultLayout(),
                EnvironmentOverrides = new Dictionary<string, HudLayout>(StringComparer.Ordinal),
            };
        }

        public static HudLayout CreateDefaultLayout()
        {
            var layout = new HudLayout();
            foreach (string elementId in VanillaElementIds.All)
            {
                layout.Vanilla[elementId] = CreateVanillaDefaults(elementId);
            }

            return layout;
        }

        public static HudLayout CreateStarterLayout()
        {
            HudLayout layout = CreateDefaultLayout();
            foreach (string typeId in StarterTypes)
            {
                CounterSettings counter = CreateCounter(typeId, Vec3.Zero, Vec3.Zero);
                PlaceInDefaultSlot(counter);
                layout.Counters.Add(counter);
            }

            return layout;
        }

        public static VanillaElementSettings CreateVanillaDefaults(string elementId)
        {
            if (elementId == null) throw new ArgumentNullException(nameof(elementId));

            return new VanillaElementSettings();
        }

        public static CounterSettings CreateCounter(string typeId, Vec3 position, Vec3 rotation)
        {
            if (typeId == null) throw new ArgumentNullException(nameof(typeId));

            var style = new TextStyle();
            CounterTypeDescriptor? descriptor = CounterCatalog.GetDescriptor(typeId);
            if (descriptor != null)
            {
                style.Decimals = Math.Min(Math.Max(descriptor.DefaultDecimals, ConfigValidator.MinDecimals), ConfigValidator.MaxDecimals);
            }

            return new CounterSettings
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = typeId,
                Enabled = true,
                Attachment = CounterAttachment.Hud,
                Position = position,
                Rotation = rotation,
                Scale = 1f,
                Style = style,
                Options = new Dictionary<string, JToken>(StringComparer.Ordinal),
            };
        }

        public static FirstReleaseUpgrade UpgradeFirstReleaseDefaults(CounterSettings? counter)
        {
            TextStyle? style = counter?.Style;
            if (style == null || !HasFirstReleaseLook(style))
            {
                return FirstReleaseUpgrade.None;
            }

            style.LabelColor = ColorValue.White;
            style.ColorByValue = false;
            if (!IsAtFirstReleaseSlot(counter!) || !TryGetUpgradeSlot(counter!.Type, out Slot slot))
            {
                return FirstReleaseUpgrade.Restyled;
            }

            style.FontSize = TextStyle.DefaultFontSize;
            style.LabelScale = TextStyle.DefaultLabelScale;
            Place(counter, slot);
            return FirstReleaseUpgrade.RestyledAndMoved;
        }

        public static bool UpgradeFirstReleaseTextSize(CounterSettings? counter)
        {
            TextStyle? style = counter?.Style;
            if (style == null || Math.Abs(style.FontSize - FirstReleaseFontSize) > TextSizeTolerance)
            {
                return false;
            }

            if (Math.Abs(style.LabelScale - FirstReleaseLabelScale) <= TextSizeTolerance)
            {
                style.LabelScale = TextStyle.DefaultLabelScale;
            }

            float scale = counter!.Scale;
            if (!float.IsNaN(scale) && !float.IsInfinity(scale))
            {
                double sameApparentSize = Math.Round(scale * (double)FirstReleaseFontSize / TextStyle.DefaultFontSize, 4);
                counter.Scale = sameApparentSize > 1d ? (float)sameApparentSize : 1f;
            }

            style.FontSize = TextStyle.DefaultFontSize;
            return true;
        }

        public static bool UpgradeSecondReleaseDefaults(CounterSettings? counter)
        {
            TextStyle? style = counter?.Style;
            if (style == null
                || counter!.Type == null
                || !SecondReleaseSlots.TryGetValue(counter.Type, out Slot oldSlot)
                || !DefaultSlots.TryGetValue(counter.Type, out Slot newSlot)
                || !HasSecondReleaseLook(style, oldSlot.Align)
                || !IsAt(counter, oldSlot))
            {
                return false;
            }

            style.FontSize = TextStyle.DefaultFontSize;
            Place(counter, newSlot);
            return true;
        }

        public static bool UpgradeThirdReleaseTextSize(CounterSettings? counter)
        {
            TextStyle? style = counter?.Style;
            if (style == null
                || Math.Abs(style.FontSize - ThirdReleaseFontSize) > TextSizeTolerance
                || Math.Abs(style.LabelScale - ThirdReleaseLabelScale) > TextSizeTolerance)
            {
                return false;
            }

            style.FontSize = TextStyle.DefaultFontSize;
            return true;
        }

        internal static void PlaceInDefaultSlot(CounterSettings counter)
        {
            if (counter?.Type != null && DefaultSlots.TryGetValue(counter.Type, out Slot slot))
            {
                Place(counter, slot);
            }
        }

        internal static bool IsAtLegacyPpSlot(CounterSettings? counter) =>
            counter?.Type != null
            && (counter.Type == LegacyCounterTypeIds.BeatLeaderPp || counter.Type == LegacyCounterTypeIds.ScoreSaberPp)
            && SecondReleaseSlots.TryGetValue(counter.Type, out Slot slot)
            && IsAt(counter, slot);

        internal static bool HasSecondReleaseLook(TextStyle? style, TextAlign alignment) =>
            style != null
            && style.FontSize == SecondReleaseFontSize
            && style.LabelScale == SecondReleaseLabelScale
            && style.LabelColor == ColorValue.White
            && style.ValueColor == ColorValue.White
            && !style.ColorByValue
            && style.ShowLabel
            && string.IsNullOrWhiteSpace(style.LabelText)
            && style.LabelPlacement == LabelPlacement.Above
            && style.Alignment == alignment
            && !style.Bold
            && !style.Italic
            && style.OutlineWidth == 0f
            && style.OutlineColor == ColorValue.Black;

        private static Slot ColumnSlot(int column, float height) => new Slot(column * ColumnOffsetMetres, height, TextAlign.Center, CounterAttachment.Hud);

        private static Slot SecondReleaseColumnSlot(int column, float height) => new Slot(
            column * ColumnOffsetMetres,
            height,
            TextAlign.Center,
            column < 0 ? CounterAttachment.LeftPanel : CounterAttachment.RightPanel);

        private static bool TryGetUpgradeSlot(string? type, out Slot slot)
        {
            slot = default;
            if (type == null)
            {
                return false;
            }

            if (DefaultSlots.TryGetValue(type, out slot))
            {
                return true;
            }

            return (type == LegacyCounterTypeIds.BeatLeaderPp || type == LegacyCounterTypeIds.ScoreSaberPp)
                && SecondReleaseSlots.TryGetValue(type, out slot);
        }

        private static void Place(CounterSettings counter, Slot slot)
        {
            counter.Position = new Vec3((float)Math.Round(slot.X, 3), (float)Math.Round(slot.Y, 3), ColumnDepthMetres);
            counter.Attachment = slot.Attachment;
            if (counter.Style != null)
            {
                counter.Style.Alignment = slot.Align;
            }
        }

        private static bool HasFirstReleaseLook(TextStyle style) =>
            style.FontSize == FirstReleaseFontSize
            && style.LabelScale == FirstReleaseLabelScale
            && style.LabelColor == FirstReleaseLabelColor
            && style.ValueColor == ColorValue.White
            && style.ColorByValue
            && style.ShowLabel
            && string.IsNullOrWhiteSpace(style.LabelText)
            && style.LabelPlacement == LabelPlacement.Above
            && style.Alignment == TextAlign.Center
            && !style.Bold
            && !style.Italic
            && style.OutlineWidth == 0f
            && style.OutlineColor == ColorValue.Black;

        private static bool IsAtFirstReleaseSlot(CounterSettings counter)
        {
            if (counter.Type == null || !FirstReleaseSlots.TryGetValue(counter.Type, out var slot))
            {
                return false;
            }

            var expected = new Vec3(
                slot.Column * ColumnOffsetMetres,
                FirstReleaseFirstRowHeightMetres - (slot.Row * FirstReleaseRowPitchMetres),
                ColumnDepthMetres);
            return counter.Attachment == CounterAttachment.None
                && Near(counter.Position, expected)
                && Near(counter.Rotation, Vec3.Zero)
                && Math.Abs(counter.Scale - 1f) <= SlotTolerance;
        }

        private static bool IsAt(CounterSettings counter, Slot slot) =>
            counter.Attachment == slot.Attachment
            && Near(counter.Position, new Vec3(slot.X, slot.Y, ColumnDepthMetres))
            && Near(counter.Rotation, Vec3.Zero)
            && Math.Abs(counter.Scale - 1f) <= SlotTolerance;

        private static bool Near(Vec3 a, Vec3 b) =>
            Math.Abs(a.X - b.X) <= SlotTolerance
            && Math.Abs(a.Y - b.Y) <= SlotTolerance
            && Math.Abs(a.Z - b.Z) <= SlotTolerance;

        private readonly struct Slot
        {
            public Slot(float x, float y, TextAlign align, CounterAttachment attachment)
            {
                X = x;
                Y = y;
                Align = align;
                Attachment = attachment;
            }

            public float X { get; }

            public float Y { get; }

            public TextAlign Align { get; }

            public CounterAttachment Attachment { get; }
        }
    }
}
