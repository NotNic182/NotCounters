using System.Diagnostics;

namespace NotCounters.Util
{
    internal static class MonotonicClock
    {
        private static readonly double SecondsPerTick = 1.0 / Stopwatch.Frequency;

        public static double Now => Stopwatch.GetTimestamp() * SecondsPerTick;
    }
}
