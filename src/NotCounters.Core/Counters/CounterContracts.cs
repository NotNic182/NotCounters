using System.Collections.Generic;
using NotCounters.Core.Config;
using NotCounters.Core.Stats;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Counters
{
    public static class VanillaElementIds
    {
        public const string Combo = "combo";
        public const string Score = "score";
        public const string Rank = "rank";
        public const string RelativeScore = "relativeScore";
        public const string Multiplier = "multiplier";
        public const string SongProgress = "songProgress";
        public const string Energy = "energy";

        public const string LeftPanelBackdrop = "leftPanelBackdrop";

        public const string RightPanelBackdrop = "rightPanelBackdrop";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Combo, Score, Rank, RelativeScore, Multiplier, SongProgress, Energy, LeftPanelBackdrop, RightPanelBackdrop,
        };
    }

    public static class CounterTypeIds
    {
        public const string Accuracy = "accuracy";
        public const string Score = "score";
        public const string Combo = "combo";
        public const string MissedNotes = "missedNotes";
        public const string FcAccuracy = "fcAccuracy";
        public const string BestPossibleAccuracy = "bestPossibleAccuracy";
        public const string PersonalBest = "personalBest";
        public const string NotesLeft = "notesLeft";

        public const string Pp = "pp";
        public const string CutAccuracy = "cutAccuracy";
        public const string TimingDeviation = "timingDeviation";
        public const string SwingSpeed = "swingSpeed";
        public const string SwingQuality = "swingQuality";
        public const string Energy = "energy";
        public const string SongTime = "songTime";
        public const string WallsHit = "wallsHit";
        public const string BombsHit = "bombsHit";
        public const string Pauses = "pauses";
        public const string MapInfo = "mapInfo";
        public const string Clock = "clock";
        public const string FailIndicator = "failIndicator";
        public const string Multiplier = "multiplier";
    }

    public enum CounterCategory
    {
        Accuracy,
        Score,
        Notes,
        Swing,
        Leaderboard,
        Health,
        Time,
        Info,
    }

    public enum CounterOptionKind
    {
        Bool,
        Int,
        Float,
        Choice,
        Text,
        Color,
    }

    public sealed class CounterOptionDescriptor
    {
        public CounterOptionDescriptor(string key, string displayName, CounterOptionKind kind, JToken defaultValue)
        {
            Key = key;
            DisplayName = displayName;
            Kind = kind;
            DefaultValue = defaultValue;
        }

        public string Key { get; }

        public string DisplayName { get; }

        public CounterOptionKind Kind { get; }

        public JToken DefaultValue { get; }

        public string? HelpText { get; set; }

        public double Min { get; set; } = double.MinValue;

        public double Max { get; set; } = double.MaxValue;

        public double Step { get; set; } = 1;

        public IReadOnlyList<KeyValuePair<string, string>> Choices { get; set; } = new KeyValuePair<string, string>[0];
    }

    public sealed class CounterTypeDescriptor
    {
        public CounterTypeDescriptor(string typeId, string displayName, CounterCategory category, string defaultLabel)
        {
            TypeId = typeId;
            DisplayName = displayName;
            Category = category;
            DefaultLabel = defaultLabel;
        }

        public string TypeId { get; }

        public string DisplayName { get; }

        public CounterCategory Category { get; }

        public string DefaultLabel { get; }

        public bool UsesRowLogos { get; set; }

        public string Description { get; set; } = string.Empty;

        public bool UsesDecimals { get; set; }

        public int DefaultDecimals { get; set; } = 2;

        public IReadOnlyList<CounterOptionDescriptor> Options { get; set; } = new CounterOptionDescriptor[0];
    }

    public readonly struct CounterRow
    {
        private readonly string? _iconKey;
        private readonly string? _value;

        public CounterRow(string iconKey, string value)
        {
            _iconKey = iconKey;
            _value = value;
        }

        public string IconKey => _iconKey ?? string.Empty;

        public string Value => _value ?? string.Empty;
    }

    public readonly struct CounterRender
    {
        public CounterRender(
            string value,
            string? labelOverride = null,
            ColorValue? valueColor = null,
            bool hidden = false,
            IReadOnlyList<CounterRow>? rows = null,
            string? detail = null,
            ColorValue? detailColor = null,
            bool reservesDetail = false)
        {
            Value = value;
            LabelOverride = labelOverride;
            ValueColor = valueColor;
            Hidden = hidden;
            Rows = rows;
            Detail = detail;
            DetailColor = detailColor;
            ReservesDetail = reservesDetail;
        }

        public string Value { get; }

        public string? LabelOverride { get; }

        public ColorValue? ValueColor { get; }

        public bool Hidden { get; }

        public IReadOnlyList<CounterRow>? Rows { get; }

        public string? Detail { get; }

        public ColorValue? DetailColor { get; }

        public bool ReservesDetail { get; }

        public static CounterRender Text(string value) => new CounterRender(value);

        public static CounterRender FromRows(IReadOnlyList<CounterRow>? rows, string? labelOverride = null, ColorValue? valueColor = null)
        {
            if (rows == null || rows.Count == 0)
            {
                return new CounterRender(string.Empty, labelOverride, valueColor);
            }

            string value;
            if (rows.Count == 1)
            {
                value = rows[0].Value;
            }
            else if (rows.Count == 2)
            {
                value = rows[0].Value + "\n" + rows[1].Value;
            }
            else
            {
                var parts = new string[rows.Count];
                for (var i = 0; i < parts.Length; i++)
                {
                    parts[i] = rows[i].Value;
                }

                value = string.Join("\n", parts);
            }

            return new CounterRender(value, labelOverride, valueColor, rows: rows);
        }

        public static readonly CounterRender Empty = new CounterRender(string.Empty, hidden: true);
    }

    public interface ICounterPresenter
    {
        CounterTypeDescriptor Descriptor { get; }

        CounterRender Render(StatsSnapshot stats, CounterSettings settings);
    }
}
