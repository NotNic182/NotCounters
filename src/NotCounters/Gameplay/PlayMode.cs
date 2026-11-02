using System;
using NotCounters.Core.Leaderboards;

namespace NotCounters.Gameplay
{
    internal readonly struct PlayMode
    {
        public const string PartyGameMode = "Party";

        private PlayMode(bool isPractice, bool isParty, bool isCampaign)
        {
            IsPractice = isPractice;
            IsParty = isParty;
            IsCampaign = isCampaign;
        }

        public bool IsPractice { get; }

        public bool IsParty { get; }

        public bool IsCampaign { get; }

        public bool IsPartyOrCampaign => IsParty || IsCampaign;

        public static PlayMode Detect(
            GameplayCoreSceneSetupData sceneSetupData,
            StandardLevelScenesTransitionSetupDataSO? standardTransitionData,
            MissionGameplaySceneSetupData? missionSetupData)
        {
            PracticeSettings? practice = sceneSetupData.practiceSettings;
            bool isPractice = practice != null
                              && PlaySpeed.IsPracticeRun(practice.startSongTime, practice.songSpeedMul, sceneSetupData.gameplayModifiers.songSpeedMul);

            bool isCampaign = missionSetupData != null;

            bool isParty = !isCampaign
                           && standardTransitionData != null
                           && ReferenceEquals(standardTransitionData.gameplayCoreSceneSetupData, sceneSetupData)
                           && string.Equals(standardTransitionData.gameMode, PartyGameMode, StringComparison.Ordinal);

            return new PlayMode(isPractice, isParty, isCampaign);
        }
    }
}
