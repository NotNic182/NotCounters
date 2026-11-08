using System;
using NotCounters.Core.Stats;

namespace NotCounters.Gameplay
{
    internal static class PersonalBestReader
    {
        public const string NoPersonalBestText = PersonalBestStatus.NoPersonalBest;

        public const string PracticeText = PersonalBestStatus.Practice;

        public const string LoadingText = PersonalBestStatus.Loading;

        public static void ReadLocal(
            PersonalBestInfo target,
            PlayerDataModel? playerDataModel,
            in BeatmapKey key,
            int fullMapMaxMultipliedScore,
            PlayMode mode)
        {
            if (mode.IsPractice)
            {
                SetUnavailable(target, PracticeText);
                return;
            }

            if (mode.IsPartyOrCampaign)
            {
                SetUnavailable(target, NoPersonalBestText);
                return;
            }

            PlayerLevelStatsData? stats = playerDataModel != null ? playerDataModel.playerData?.TryGetPlayerLevelStatsData(in key) : null;
            if (stats == null || !stats.validScore)
            {
                SetUnavailable(target, NoPersonalBestText);
                return;
            }

            target.Available = true;
            target.Source = PbSource.Local;
            target.Score = stats.highScore;
            target.Accuracy = fullMapMaxMultipliedScore > 0
                ? Math.Max(0d, stats.highScore / (double)fullMapMaxMultipliedScore)
                : 0d;
            target.MaxCombo = stats.maxCombo;
            target.FullCombo = stats.fullCombo;
            target.StatusText = null;
        }

        public static void CopyInto(PersonalBestInfo target, PersonalBestInfo source)
        {
            target.Available = source.Available;
            target.Source = source.Source;
            target.Score = source.Score;
            target.Accuracy = source.Accuracy;
            target.MaxCombo = source.MaxCombo;
            target.FullCombo = source.FullCombo;
            target.StatusText = source.StatusText;
            target.ScoreId = source.ScoreId;
            target.Line = source.Line;
        }

        public static void SetUnavailable(PersonalBestInfo target, string statusText, PbSource source = PbSource.Local)
        {
            target.Available = false;
            target.Source = source;
            target.Score = 0;
            target.Accuracy = 0d;
            target.MaxCombo = 0;
            target.FullCombo = false;
            target.StatusText = statusText;
            target.ScoreId = null;
            target.Line = null;
        }
    }
}
