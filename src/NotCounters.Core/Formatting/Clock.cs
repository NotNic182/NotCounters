using System;

namespace NotCounters.Core.Formatting
{
    public static class Clock
    {
        private static readonly Func<DateTime> SystemNow = () => DateTime.Now;

        private static Func<DateTime> now = SystemNow;

        public static Func<DateTime> Now
        {
            get => now;
            set => now = value ?? SystemNow;
        }

        public static void Reset() => now = SystemNow;
    }
}
