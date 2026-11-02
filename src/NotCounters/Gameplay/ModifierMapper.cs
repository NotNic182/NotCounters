using NotCounters.Core.Leaderboards;

namespace NotCounters.Gameplay
{
    internal static class ModifierMapper
    {
        public static ModifierSet ToModifierSet(GameplayModifiers modifiers, SongSpeed songSpeed) => new ModifierSet
        {
            NoFail = modifiers.noFailOn0Energy,
            InstaFail = modifiers.instaFail,
            FourLives = modifiers.energyType == GameplayModifiers.EnergyType.Battery,
            NoWalls = modifiers.enabledObstacleType == GameplayModifiers.EnabledObstacleType.NoObstacles,
            NoBombs = modifiers.noBombs,
            NoArrows = modifiers.noArrows,
            GhostNotes = modifiers.ghostNotes,
            DisappearingArrows = modifiers.disappearingArrows,
            SmallNotes = modifiers.smallCubes,
            ProMode = modifiers.proMode,
            StrictAngles = modifiers.strictAngles,
            ZenMode = modifiers.zenMode,
            SongSpeed = songSpeed,
        };

        public static bool HasModifierWithoutLeaderboardCode(GameplayModifiers modifiers) =>
            modifiers.fastNotes || modifiers.failOnSaberClash;
    }
}
