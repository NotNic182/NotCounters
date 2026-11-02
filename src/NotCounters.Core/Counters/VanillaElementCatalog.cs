using System;
using System.Collections.Generic;
using NotCounters.Core.Config;

namespace NotCounters.Core.Counters
{
    public sealed class VanillaElementDescriptor
    {
        public string Id { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public bool SupportsPose { get; init; } = true;

        public bool SupportsPrimaryColor { get; init; }

        public string PrimaryColorLabel { get; init; } = string.Empty;

        public bool SupportsSecondaryColor { get; init; }

        public string SecondaryColorLabel { get; init; } = string.Empty;

        public bool SupportsFontSize { get; init; }

        public bool SupportsLabelText { get; init; }

        public bool SupportsDecimals { get; init; }

        public bool SupportsNumberGrouping { get; init; }

        public bool SupportsTimeMode { get; init; }

        public bool SupportsHideFullComboLines { get; init; }

        public bool SupportsOutline { get; init; }
    }

    public static class VanillaElementCatalog
    {
        private const string TextColor = "Text color";

        private const string BackdropColor = "Backdrop color";

        private static readonly VanillaElementDescriptor[] Descriptors =
        {
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.Combo,
                DisplayName = "Combo",
                SupportsPrimaryColor = true,
                PrimaryColorLabel = TextColor,
                SupportsSecondaryColor = true,
                SecondaryColorLabel = "Full-combo lines color",
                SupportsFontSize = true,
                SupportsLabelText = true,
                SupportsHideFullComboLines = true,
                SupportsOutline = true,
            },
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.Score,
                DisplayName = "Score",
                SupportsPrimaryColor = true,
                PrimaryColorLabel = TextColor,
                SupportsFontSize = true,
                SupportsNumberGrouping = true,
                SupportsOutline = true,
            },
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.Rank,
                DisplayName = "Rank",
                SupportsPrimaryColor = true,
                PrimaryColorLabel = TextColor,
                SupportsFontSize = true,
                SupportsOutline = true,
            },
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.RelativeScore,
                DisplayName = "Accuracy (relative score)",
                SupportsPrimaryColor = true,
                PrimaryColorLabel = TextColor,
                SupportsFontSize = true,
                SupportsDecimals = true,
                SupportsOutline = true,
            },
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.Multiplier,
                DisplayName = "Multiplier",
                SupportsPrimaryColor = true,
                PrimaryColorLabel = TextColor,
                SupportsSecondaryColor = true,
                SecondaryColorLabel = "Ring color",
                SupportsFontSize = true,
            },
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.SongProgress,
                DisplayName = "Song progress",
                SupportsPrimaryColor = true,
                PrimaryColorLabel = "Bar color",
                SupportsSecondaryColor = true,
                SecondaryColorLabel = TextColor,
                SupportsFontSize = true,
                SupportsTimeMode = true,
            },
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.Energy,
                DisplayName = "Energy bar",
                SupportsPrimaryColor = true,
                PrimaryColorLabel = "Bar color",
                SupportsSecondaryColor = true,
                SecondaryColorLabel = "Background color",
            },
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.LeftPanelBackdrop,
                DisplayName = "Left panel backdrop",
                SupportsPose = false,
                SupportsPrimaryColor = true,
                PrimaryColorLabel = BackdropColor,
            },
            new VanillaElementDescriptor
            {
                Id = VanillaElementIds.RightPanelBackdrop,
                DisplayName = "Right panel backdrop",
                SupportsPose = false,
                SupportsPrimaryColor = true,
                PrimaryColorLabel = BackdropColor,
            },
        };

        private static readonly Dictionary<string, VanillaElementDescriptor> ById = BuildIndex();

        public static IReadOnlyList<VanillaElementDescriptor> All => Descriptors;

        public static VanillaElementDescriptor? Get(string id) => id != null && ById.TryGetValue(id, out var descriptor) ? descriptor : null;

        private static Dictionary<string, VanillaElementDescriptor> BuildIndex()
        {
            var index = new Dictionary<string, VanillaElementDescriptor>(StringComparer.Ordinal);
            foreach (var descriptor in Descriptors)
            {
                index[descriptor.Id] = descriptor;
            }

            return index;
        }
    }
}
