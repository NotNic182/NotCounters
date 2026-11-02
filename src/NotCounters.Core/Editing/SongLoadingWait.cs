using System;

namespace NotCounters.Core.Editing
{
    public enum SongLoadingWaitStep
    {
        Proceed,
        Wait,
        TimedOut,
    }

    public sealed class SongLoadingWait
    {
        private double? _idleSince;

        public SongLoadingWait(double minimumSeconds, double settleSeconds, double timeoutSeconds, double finalStepTimeoutSeconds)
        {
            TimeoutSeconds = NonNegative(timeoutSeconds);
            FinalStepTimeoutSeconds = Math.Max(NonNegative(finalStepTimeoutSeconds), TimeoutSeconds);
            MinimumSeconds = Math.Min(NonNegative(minimumSeconds), TimeoutSeconds);
            SettleSeconds = Math.Min(NonNegative(settleSeconds), TimeoutSeconds);
        }

        public double MinimumSeconds { get; }

        public double SettleSeconds { get; }

        public double TimeoutSeconds { get; }

        public double FinalStepTimeoutSeconds { get; }

        public bool SawLoading { get; private set; }

        public bool SawFinalStep { get; private set; }

        public SongLoadingWaitStep Next(bool songsLoading, bool finalStep, double elapsedSeconds)
        {
            var now = NonNegative(elapsedSeconds);
            if (songsLoading)
            {
                SawLoading = true;
                SawFinalStep |= finalStep;
                _idleSince = null;
                var limit = finalStep ? FinalStepTimeoutSeconds : TimeoutSeconds;
                return now >= limit ? SongLoadingWaitStep.TimedOut : SongLoadingWaitStep.Wait;
            }

            _idleSince ??= now;
            var requiredIdle = SawLoading ? SettleSeconds : 0d;
            return now >= MinimumSeconds && now - _idleSince.Value >= requiredIdle
                ? SongLoadingWaitStep.Proceed
                : SongLoadingWaitStep.Wait;
        }

        private static double NonNegative(double seconds) => double.IsNaN(seconds) || seconds < 0d ? 0d : seconds;
    }
}
