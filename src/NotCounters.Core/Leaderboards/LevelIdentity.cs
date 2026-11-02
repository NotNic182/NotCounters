using System;
using System.Diagnostics.CodeAnalysis;

namespace NotCounters.Core.Leaderboards
{
    public sealed class LevelIdentity : IEquatable<LevelIdentity>
    {
        public const string CustomLevelPrefix = "custom_level_";

        public const int HashLength = 40;

        public LevelIdentity(string? hash, string? characteristic, string? difficultyName)
        {
            Hash = (hash ?? string.Empty).Trim().ToUpperInvariant();
            Characteristic = (characteristic ?? string.Empty).Trim();
            string raw = (difficultyName ?? string.Empty).Trim();
            DifficultyName = NormalizeDifficultyName(raw) ?? raw;
        }

        public string Hash { get; }

        public string Characteristic { get; }

        public string DifficultyName { get; }

        public bool IsValid =>
            Hash.Length == HashLength && IsHash(Hash, 0) && Characteristic.Length > 0 && DifficultyNameToScoreSaberInt(DifficultyName) != 0;

        public string BeatLeaderDifficultySegment => DifficultyName;

        public string BeatLeaderModeSegment => Characteristic;

        public string? ScoreSaberGameMode => CharacteristicToScoreSaberGameMode(Characteristic);

        public int ScoreSaberDifficulty => DifficultyNameToScoreSaberInt(DifficultyName);

        public static bool TryCreate(string? levelId, string? characteristic, string? difficultyName, [NotNullWhen(true)] out LevelIdentity? identity)
        {
            identity = null;
            if (!TryParseHashFromLevelId(levelId, out string hash)) return false;
            var candidate = new LevelIdentity(hash, characteristic, difficultyName);
            if (!candidate.IsValid) return false;
            identity = candidate;
            return true;
        }

        public static bool TryParseHashFromLevelId(string? levelId, out string hash)
        {
            hash = string.Empty;
            if (string.IsNullOrEmpty(levelId)) return false;
            string id = levelId!.Trim();
            int start = id.StartsWith(CustomLevelPrefix, StringComparison.OrdinalIgnoreCase) ? CustomLevelPrefix.Length : 0;
            if (!IsHash(id, start)) return false;
            if (start == 0 && id.Length != HashLength) return false;
            if (id.Length > start + HashLength && IsHex(id[start + HashLength])) return false;
            hash = id.Substring(start, HashLength).ToUpperInvariant();
            return true;
        }

        public static string? NormalizeDifficultyName(string? difficultyName)
        {
            if (string.IsNullOrWhiteSpace(difficultyName)) return null;
            string key = difficultyName!.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            switch (key)
            {
                case "easy": return "Easy";
                case "normal": return "Normal";
                case "hard": return "Hard";
                case "expert": return "Expert";
                case "expertplus":
                case "expert+": return "ExpertPlus";
                default: return null;
            }
        }

        public static int DifficultyNameToScoreSaberInt(string? difficultyName)
        {
            switch (NormalizeDifficultyName(difficultyName))
            {
                case "Easy": return 1;
                case "Normal": return 3;
                case "Hard": return 5;
                case "Expert": return 7;
                case "ExpertPlus": return 9;
                default: return 0;
            }
        }

        public static string? CharacteristicToScoreSaberGameMode(string? characteristic)
        {
            if (string.IsNullOrWhiteSpace(characteristic)) return null;
            string name = characteristic!.Trim();
            return name.StartsWith("Solo", StringComparison.Ordinal) ? name : "Solo" + name;
        }

        public bool Equals(LevelIdentity? other) =>
            other != null
            && string.Equals(Hash, other.Hash, StringComparison.Ordinal)
            && string.Equals(Characteristic, other.Characteristic, StringComparison.OrdinalIgnoreCase)
            && string.Equals(DifficultyName, other.DifficultyName, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is LevelIdentity other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = StringComparer.Ordinal.GetHashCode(Hash);
                h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Characteristic);
                return (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(DifficultyName);
            }
        }

        public override string ToString() => Hash + "/" + Characteristic + "/" + DifficultyName;

        private static bool IsHash(string s, int start)
        {
            if (s.Length < start + HashLength) return false;
            for (int i = start; i < start + HashLength; i++)
            {
                if (!IsHex(s[i])) return false;
            }

            return true;
        }

        private static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
