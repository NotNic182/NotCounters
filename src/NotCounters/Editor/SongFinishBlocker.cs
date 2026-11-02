using System;
using NotCounters.Contracts;
using NotCounters.Util;
using SiraUtil.Affinity;

namespace NotCounters.Editor
{
    internal sealed class SongFinishBlocker : IAffinity
    {
        private readonly IEditSessionState _state;

        public SongFinishBlocker(IEditSessionState state)
        {
            _state = state;
        }

        [AffinityPrefix]
        [AffinityPatch(typeof(StandardLevelGameplayManager), nameof(StandardLevelGameplayManager.HandleSongDidFinish))]
        private bool BlockSongFinishDuringEditSession()
        {
            var sessionActive = false;
            try
            {
                sessionActive = _state.IsActive;
                if (sessionActive)
                {
                    Safe.WarnOnce("SongFinishBlocker.Blocked", "NotCounters blocked the song-finished handler of an edit session.");
                }

                return !sessionActive;
            }
            catch (Exception ex)
            {
                Safe.Report("SongFinishBlocker", ex);
                return !sessionActive;
            }
        }
    }
}
