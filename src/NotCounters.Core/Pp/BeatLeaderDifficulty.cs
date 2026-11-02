using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NotCounters.Core.Pp
{
    public enum BeatLeaderStatus
    {
        Unknown = -1,
        Unranked = 0,
        Nominated = 1,
        Qualified = 2,
        Ranked = 3,
        Unrankable = 4,
        Outdated = 5,
        InEvent = 6,
        Ost = 7,
    }

    public sealed record BeatLeaderDifficulty
    {
        private readonly IReadOnlyDictionary<string, double>? _modifierValues;
        private readonly IReadOnlyDictionary<string, double?>? _modifiersRating;

        public string? LeaderboardId { get; init; }

        public int Status { get; init; } = (int)BeatLeaderStatus.Unknown;

        public BeatLeaderStatus StatusKind =>
            Status >= 0 && Status <= 7 ? (BeatLeaderStatus)Status : BeatLeaderStatus.Unknown;

        public double? Stars { get; init; }

        public double? AccRating { get; init; }

        public double? PassRating { get; init; }

        public double? TechRating { get; init; }

        public double? PredictedAcc { get; init; }

        public double? SliderPercentage { get; init; }

        public int Notes { get; init; }

        public double? NoteSpan { get; init; }

        public int MaxScore { get; init; }

        public string? ModeName { get; init; }

        public string? DifficultyName { get; init; }

        public int? Value { get; init; }

        public IReadOnlyDictionary<string, double>? ModifierValues
        {
            get => _modifierValues;
            init => _modifierValues = CopyValues(value);
        }

        public IReadOnlyDictionary<string, double?>? ModifiersRating
        {
            get => _modifiersRating;
            init => _modifiersRating = CopyRatings(value);
        }

        public bool IsReweighted => NoteSpan.HasValue;

        public bool HasNoRatings => !AccRating.HasValue && !PassRating.HasValue && !TechRating.HasValue;

        public double? GetModifierRating(string code, string suffix)
        {
            if (_modifiersRating == null || string.IsNullOrEmpty(code) || suffix == null) return null;
            return _modifiersRating.TryGetValue(code.ToLowerInvariant() + suffix, out double? v) ? v : null;
        }

        public bool HasModifierRating(string code, string suffix)
        {
            if (_modifiersRating == null || string.IsNullOrEmpty(code) || suffix == null) return false;
            return _modifiersRating.ContainsKey(code.ToLowerInvariant() + suffix);
        }

        private static IReadOnlyDictionary<string, double>? CopyValues(IReadOnlyDictionary<string, double>? source)
        {
            if (source == null) return null;
            var copy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> kv in source)
            {
                if (kv.Key == null || string.Equals(kv.Key, "modifierId", StringComparison.OrdinalIgnoreCase)) continue;
                if (!PpNumerics.IsFinite(kv.Value)) continue;
                copy[kv.Key.ToLowerInvariant()] = kv.Value;
            }

            return new ReadOnlyDictionary<string, double>(copy);
        }

        private static IReadOnlyDictionary<string, double?>? CopyRatings(IReadOnlyDictionary<string, double?>? source)
        {
            if (source == null) return null;
            var copy = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double?> kv in source)
            {
                if (kv.Key == null) continue;
                double? v = kv.Value.HasValue && PpNumerics.IsFinite(kv.Value.Value) ? kv.Value : null;
                copy[kv.Key] = v;
            }

            return new ReadOnlyDictionary<string, double?>(copy);
        }
    }
}
