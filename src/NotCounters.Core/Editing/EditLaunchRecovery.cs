namespace NotCounters.Core.Editing
{
    public static class EditLaunchRecovery
    {
        public const double SettleSeconds = 1.0;

        public const double SongLoadingTimeoutSeconds = 15.0;

        public const double SongLoadingFinalStepTimeoutSeconds = 60.0;

        public static bool IsSongAudioInvalid(bool clipMissing, bool loadFailed, int samples, float lengthSeconds) =>
            clipMissing || loadFailed || samples == 0 || lengthSeconds == 0f;

        public static bool SongNeverStarted(float endSongTime) => endSongTime <= 0f;

        public static bool IsAudioLoadFailure(bool endedByQuit, float endSongTime, bool songAudioInvalid) =>
            endedByQuit && songAudioInvalid && SongNeverStarted(endSongTime);

        public static bool ShouldRetry(bool retryAvailable, bool relaunchRequested, bool audioLoadFailure) =>
            retryAvailable && !relaunchRequested && audioLoadFailure;
    }
}
