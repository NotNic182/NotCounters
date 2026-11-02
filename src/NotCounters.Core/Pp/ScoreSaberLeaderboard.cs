using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NotCounters.Core.Pp
{
    public enum ScoreSaberLeaderboardStatus
    {
        Unranked = 0,
        Ranked = 1,
        Qualified = 2,
        Loved = 3,
        Unknown = 4,
    }

    public enum ScoreSaberPlayOutcome
    {
        Clear,
        Fail,
        Quit,
        Restart,
        Unknown,
    }

    public sealed record ScoreSaberLeaderboard
    {
        private readonly IReadOnlyDictionary<string, double>? _modifierValues;

        public long Id { get; init; }

        public ScoreSaberLeaderboardStatus Status { get; init; } = ScoreSaberLeaderboardStatus.Unknown;

        public double Stars { get; init; }

        public long MaxScore { get; init; }

        public bool PositiveModifiers { get; init; }

        public IReadOnlyDictionary<string, double>? ModifierValues
        {
            get => _modifierValues;
            init => _modifierValues = Copy(value);
        }

        public bool ModifierValuesAreDefaults { get; init; }

        public int RealmId { get; init; } = 1;

        public double? MaxPp { get; init; }

        public double? V1MaxPp { get; init; }

        public string? Hash { get; init; }

        public int Difficulty { get; init; }

        public string? GameMode { get; init; }

        public bool IsRanked => Status == ScoreSaberLeaderboardStatus.Ranked;

        private static IReadOnlyDictionary<string, double>? Copy(IReadOnlyDictionary<string, double>? source)
        {
            if (source == null) return null;
            var copy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> kv in source)
            {
                if (string.IsNullOrEmpty(kv.Key) || !PpNumerics.IsFinite(kv.Value)) continue;
                copy[kv.Key.Trim().ToUpperInvariant()] = kv.Value;
            }

            return new ReadOnlyDictionary<string, double>(copy);
        }
    }

    public sealed record ScoreSaberScoreSample
    {
        public long Id { get; init; }

        public long? UnmodifiedScore { get; init; }

        public long ModifiedScore { get; init; }

        public double Pp { get; init; }

        private readonly IReadOnlyList<string> _mods = Array.Empty<string>();

        public IReadOnlyList<string> Mods
        {
            get => _mods;
            init => _mods = value ?? Array.Empty<string>();
        }

        public ScoreSaberPlayOutcome Outcome { get; init; } = ScoreSaberPlayOutcome.Unknown;
    }
}
