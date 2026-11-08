using System;
using System.Collections.Generic;
using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters
{
    public static class CounterChoices
    {
        private static readonly IReadOnlyList<PbSource> AutoPbSources = Array.AsReadOnly(new[] { PbSource.BeatLeader, PbSource.ScoreSaber, PbSource.Local });
        private static readonly IReadOnlyList<PbSource> BeatLeaderPbSources = Array.AsReadOnly(new[] { PbSource.BeatLeader });
        private static readonly IReadOnlyList<PbSource> ScoreSaberPbSources = Array.AsReadOnly(new[] { PbSource.ScoreSaber });
        private static readonly IReadOnlyList<PbSource> LocalPbSources = Array.AsReadOnly(new[] { PbSource.Local });

        public const string ScoreModified = "modified";

        public const string ScoreMultiplied = "multiplied";

        public const string GroupingNone = "none";

        public const string GroupingSpace = "space";

        public const string GroupingComma = "comma";

        public const string GroupingDot = "dot";

        public const string MissCombined = "combined";

        public const string MissMisses = "misses";

        public const string MissBadCuts = "badCuts";

        public const string MissSeparate = "separate";

        public const string FcDisplayValue = "value";

        public const string FcDisplayValueAndDifference = "valueAndDifference";

        public const string FcDisplayDifference = "difference";

        public const string PbSourceAuto = "auto";

        public const string PbSourceLocal = "local";

        public const string PbSourceBeatLeader = "beatLeader";

        public const string PbSourceScoreSaber = "scoreSaber";

        public const string NotesLeftLeft = "left";

        public const string NotesLeftLeftTotal = "leftTotal";

        public const string NotesLeftHitTotal = "hitTotal";

        public const string TimingSigned = "signed";

        public const string TimingAbsolute = "absolute";

        public const string SpeedAverage = "average";

        public const string SpeedRecent = "recent";

        public const string SwingPercent = "percent";

        public const string SwingUnderswings = "underswings";

        public const string SwingBoth = "both";

        public const string LivesText = "text";

        public const string LivesIcons = "icons";

        public const string TimeElapsed = "elapsed";

        public const string TimeRemaining = "remaining";

        public const string TimeElapsedTotal = "elapsedTotal";

        public const string TimeTotal = "total";

        public const string TimePercent = "percent";

        public const string PpBoth = "both";

        public const string PpBeatLeader = "beatLeader";

        public const string PpScoreSaber = "scoreSaber";

        public const string HandColorsOff = "off";

        public const string HandColorsSabers = "sabers";

        public const string HandColorsCustom = "custom";

        public static PbSource ToPbSource(string? choice)
        {
            if (string.Equals(choice, PbSourceBeatLeader, StringComparison.Ordinal))
            {
                return PbSource.BeatLeader;
            }

            return string.Equals(choice, PbSourceScoreSaber, StringComparison.Ordinal) ? PbSource.ScoreSaber : PbSource.Local;
        }

        public static IReadOnlyList<PbSource> PbSourceCandidates(string? choice)
        {
            if (string.Equals(choice, PbSourceAuto, StringComparison.Ordinal))
            {
                return AutoPbSources;
            }

            switch (ToPbSource(choice))
            {
                case PbSource.BeatLeader: return BeatLeaderPbSources;
                case PbSource.ScoreSaber: return ScoreSaberPbSources;
                default: return LocalPbSources;
            }
        }

        public static NumberGrouping ToNumberGrouping(string? choice)
        {
            switch (choice)
            {
                case GroupingSpace: return NumberGrouping.Space;
                case GroupingComma: return NumberGrouping.Comma;
                case GroupingDot: return NumberGrouping.Dot;
                default: return NumberGrouping.None;
            }
        }
    }
}
