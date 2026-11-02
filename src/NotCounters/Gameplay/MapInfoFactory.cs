using NotCounters.Core.Stats;

namespace NotCounters.Gameplay
{
    internal static class MapInfoFactory
    {
        public static void Fill(MapInfo target, GameplayCoreSceneSetupData sceneSetupData, float? noteJumpSpeed)
        {
            BeatmapKey key = sceneSetupData.beatmapKey;
            BeatmapLevel? level = sceneSetupData.beatmapLevel;
            BeatmapBasicData? basicData = sceneSetupData.beatmapBasicData;

            target.SongName = level?.songName ?? string.Empty;
            target.SongSubName = level?.songSubName ?? string.Empty;
            target.SongAuthor = level?.songAuthorName ?? string.Empty;
            target.Mapper = JoinNames(basicData?.mappers, level?.allMappers);
            target.DifficultyName = LevelIdentityFactory.DifficultyName(key.difficulty);
            target.Characteristic = LevelIdentityFactory.CharacteristicName(in key);
            target.Bpm = level?.beatsPerMinute ?? 0f;
            target.NoteJumpSpeed = noteJumpSpeed ?? basicData?.noteJumpMovementSpeed ?? 0f;
            target.Hash = LevelIdentityFactory.TryGetHash(key.levelId);
        }

        private static string JoinNames(string[]? difficultyNames, string[]? levelNames)
        {
            string[]? names = difficultyNames != null && difficultyNames.Length > 0 ? difficultyNames : levelNames;
            return names == null || names.Length == 0 ? string.Empty : string.Join(", ", names);
        }
    }
}
