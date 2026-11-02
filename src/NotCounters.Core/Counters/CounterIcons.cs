namespace NotCounters.Core.Counters
{
    public static class CounterIcons
    {
        public const string BeatLeader = "beatleader";

        public const string ScoreSaber = "scoresaber";

        public static string? BadgeText(string? iconKey)
        {
            switch (iconKey)
            {
                case BeatLeader: return "BL";
                case ScoreSaber: return "SS";
                default: return null;
            }
        }
    }
}
