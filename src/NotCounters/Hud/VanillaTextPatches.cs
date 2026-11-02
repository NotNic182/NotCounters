using System;
using NotCounters.Util;
using SiraUtil.Affinity;

namespace NotCounters.Hud
{
    internal sealed class VanillaTextPatches : IAffinity
    {
        private readonly VanillaTextFormatter _formatter;

        public VanillaTextPatches(VanillaTextFormatter formatter)
        {
            _formatter = formatter;
        }

        [AffinityPostfix]
        [AffinityPatch(typeof(ScoreUIController), nameof(ScoreUIController.UpdateScore), AffinityMethodType.Normal, null, typeof(int))]
        private void AfterUpdateScore(ScoreUIController __instance, int displayScore)
        {
            try
            {
                _formatter.AfterScoreWritten(__instance, displayScore);
            }
            catch (Exception ex)
            {
                Safe.Report("hud:patch-score", ex);
            }
        }

        [AffinityPostfix]
        [AffinityPatch(typeof(ImmediateRankUIPanel), nameof(ImmediateRankUIPanel.RefreshUI))]
        private void AfterRefreshUI(ImmediateRankUIPanel __instance)
        {
            try
            {
                _formatter.AfterRankPanelRefreshed(__instance);
            }
            catch (Exception ex)
            {
                Safe.Report("hud:patch-relative", ex);
            }
        }

        [AffinityPostfix]
        [AffinityPatch(typeof(SongProgressUIController), nameof(SongProgressUIController.UpdateSongProgressUIElements))]
        private void AfterUpdateSongProgress(SongProgressUIController __instance)
        {
            try
            {
                _formatter.AfterSongProgressWritten(__instance);
            }
            catch (Exception ex)
            {
                Safe.Report("hud:patch-progress", ex);
            }
        }
    }
}
