using System.Diagnostics.CodeAnalysis;
using NotCounters.Core.Leaderboards;

namespace NotCounters.Gameplay
{
    internal static class LevelIdentityFactory
    {
        public static string DifficultyName(BeatmapDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BeatmapDifficulty.Easy: return "Easy";
                case BeatmapDifficulty.Normal: return "Normal";
                case BeatmapDifficulty.Hard: return "Hard";
                case BeatmapDifficulty.Expert: return "Expert";
                case BeatmapDifficulty.ExpertPlus: return "ExpertPlus";
                default: return difficulty.ToString();
            }
        }

        public static string CharacteristicName(in BeatmapKey key) =>
            key.beatmapCharacteristic != null ? key.beatmapCharacteristic.serializedName ?? string.Empty : string.Empty;

        public static string? TryGetHash(string? levelId) =>
            LevelIdentity.TryParseHashFromLevelId(levelId, out string hash) ? hash : null;

        public static bool TryCreate(in BeatmapKey key, [NotNullWhen(true)] out LevelIdentity? identity)
        {
            identity = null;
            string? hash = TryGetHash(key.levelId);
            if (hash == null) return false;

            var candidate = new LevelIdentity(hash, CharacteristicName(in key), DifficultyName(key.difficulty));
            if (!candidate.IsValid) return false;
            identity = candidate;
            return true;
        }
    }
}
