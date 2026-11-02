using NotCounters.Core.Formatting;

namespace NotCounters.Core.Leaderboards
{
    public static class PersonalBestTiming
    {
        public const double UploadSettleSeconds = 10.0;

        public const double CacheBustSeconds = 90.0;

        public static double WaitSeconds(double now, double? lastClearAt)
        {
            if (!lastClearAt.HasValue || !NumberFormatting.IsFinite(now) || !NumberFormatting.IsFinite(lastClearAt.Value)) return 0d;
            double since = now - lastClearAt.Value;
            if (since >= UploadSettleSeconds) return 0d;
            return since <= 0d ? UploadSettleSeconds : UploadSettleSeconds - since;
        }

        public static bool SkipsCachedCopy(double now, double? lastClearAt)
        {
            if (!lastClearAt.HasValue || !NumberFormatting.IsFinite(now) || !NumberFormatting.IsFinite(lastClearAt.Value)) return false;
            return now - lastClearAt.Value < CacheBustSeconds;
        }

        public static long? CacheBuster(double now, double? lastClearAt, long utcNowMs) =>
            utcNowMs > 0 && SkipsCachedCopy(now, lastClearAt) ? utcNowMs : (long?)null;
    }
}
