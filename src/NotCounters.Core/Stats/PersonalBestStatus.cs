using System;

namespace NotCounters.Core.Stats
{
    public static class PersonalBestStatus
    {
        public const string NoPersonalBest = "No PB";

        public const string Practice = "Practice";

        public const string Loading = "Loading";

        public const string DetectingAccount = "Detecting account";

        public const string NoAccount = "No account found";

        public const string AmbiguousAccount = "Several accounts";

        public const string Disabled = "Disabled";

        public const string Unavailable = "Error";

        public static bool IsPending(PersonalBestInfo? entry) =>
            entry != null
            && !entry.Available
            && (string.Equals(entry.StatusText, Loading, StringComparison.Ordinal)
                || string.Equals(entry.StatusText, DetectingAccount, StringComparison.Ordinal));
    }
}
