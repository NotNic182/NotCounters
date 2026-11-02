using System;
using NotCounters.Core.Diagnostics;
using IPALogger = IPA.Logging.Logger;

namespace NotCounters.Util
{
    internal static class Safe
    {
        private static readonly RepeatLogGate Occurrences = new RepeatLogGate();
        private static readonly object Gate = new object();

        internal static IPALogger? Logger { get; set; }

        public static void Invoke(string key, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Report(key, ex);
            }
        }

        public static T Invoke<T>(string key, Func<T> func, T fallback)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                Report(key, ex);
                return fallback;
            }
        }

        public static void Report(string key, Exception ex)
        {
            int occurrence;
            lock (Gate)
            {
                occurrence = Occurrences.Count(key + "|" + ex.GetType().FullName);
            }

            var log = Logger;
            if (log == null)
            {
                return;
            }

            try
            {
                if (occurrence == 1)
                {
                    log.Error($"[{key}] {ex.GetType().Name}: {ex.Message}");
                    log.Error(ex);
                }
                else if (RepeatLogGate.IsLoggedRepeat(occurrence))
                {
                    log.Debug($"[{key}] {ex.GetType().Name} again ({occurrence} times so far): {ex.Message}");
                }
            }
            catch
            {
            }
        }

        public static void WarnOnce(string key, string message)
        {
            bool first;
            lock (Gate)
            {
                first = Occurrences.Count("warn:" + key) == 1;
            }

            if (first)
            {
                try
                {
                    Logger?.Warn(message);
                }
                catch
                {
                }
            }
        }

        public static void ResetLogOnce()
        {
            lock (Gate)
            {
                Occurrences.Clear();
            }
        }
    }
}
