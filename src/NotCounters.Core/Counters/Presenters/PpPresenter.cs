using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Pp;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class PpPresenter : CounterPresenter
    {
        private const string Unit = "pp";

        private const string PracticeTitle = "PP (practice)";

        public PpPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.Pp, "PP", CounterCategory.Leaderboard, "PP")
            {
                Description = "Live BeatLeader and ScoreSaber pp for your current accuracy on ranked maps, each line with its leaderboard's logo.",
                UsesDecimals = true,
                DefaultDecimals = 2,
                UsesRowLogos = true,
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Leaderboards,
                        "Leaderboards",
                        CounterChoices.PpBoth,
                        "Which leaderboards to show, each on its own line with its logo.",
                        OptionBuilder.Item(CounterChoices.PpBoth, "BeatLeader and ScoreSaber"),
                        OptionBuilder.Item(CounterChoices.PpBeatLeader, "BeatLeader only"),
                        OptionBuilder.Item(CounterChoices.PpScoreSaber, "ScoreSaber only")),
                    OptionBuilder.Bool(CounterOptionKeys.ShowMaxPp, "Show max pp", false, "Append the pp for 100 % accuracy: \"345.67 / 412.33pp\"."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowFcPp, "Show FC pp", false, "Add a line with the pp you would get at your current full-combo accuracy."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowStars, "Show stars", false, "Add a line with each leaderboard's star rating."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowComponents, "Show BeatLeader components", false, "Add a line under the BeatLeader pp with its accuracy, pass and tech parts."),
                    OptionBuilder.ShowUnit(Unit),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var title = stats.IsPractice ? PracticeTitle : null;
            var beatLeader = stats.BeatLeader;
            var scoreSaber = stats.ScoreSaber;

            var choice = CounterOptions.GetChoice(settings, CounterOptionKeys.Leaderboards);
            var showBeatLeader = choice != CounterChoices.PpScoreSaber;
            var showScoreSaber = choice != CounterChoices.PpBeatLeader;
            if (showBeatLeader && showScoreSaber)
            {
                var beatLeaderDisabled = IsDisabled(beatLeader);
                if (beatLeaderDisabled != IsDisabled(scoreSaber))
                {
                    showBeatLeader = !beatLeaderDisabled;
                    showScoreSaber = beatLeaderDisabled;
                }
            }

            var format = new LineFormat(
                Decimals(settings),
                CounterOptions.GetBool(settings, CounterOptionKeys.ShowUnit) ? Unit : string.Empty,
                CounterOptions.GetBool(settings, CounterOptionKeys.ShowMaxPp),
                CounterOptions.GetBool(settings, CounterOptionKeys.ShowFcPp),
                CounterOptions.GetBool(settings, CounterOptionKeys.ShowStars));

            var rows = new CounterRow[showBeatLeader && showScoreSaber ? 2 : 1];
            var index = 0;
            if (showBeatLeader)
            {
                var components = CounterOptions.GetBool(settings, CounterOptionKeys.ShowComponents);
                rows[index++] = new CounterRow(CounterIcons.BeatLeader, Line(beatLeader, format, components));
            }

            if (showScoreSaber)
            {
                rows[index] = new CounterRow(CounterIcons.ScoreSaber, Line(scoreSaber, format, false));
            }

            return CounterRender.FromRows(rows, title);
        }

        internal static string? StatusWord(PpStatus status)
        {
            switch (status)
            {
                case PpStatus.Ranked:
                case PpStatus.Qualified:
                case PpStatus.InEvent:
                    return null;
                case PpStatus.Loading: return "Loading";
                case PpStatus.Unranked: return "Unranked";
                case PpStatus.Loved: return "Loved";
                case PpStatus.NotFound: return "No leaderboard";
                case PpStatus.Error: return "Error";
                case PpStatus.Disabled: return "Disabled";
                case PpStatus.Incompatible: return "N/A";
                case PpStatus.Failed: return "Failed";
                default: return NoData;
            }
        }

        private static string Line(LeaderboardPpInfo? info, in LineFormat format, bool showComponents)
        {
            if (info == null)
            {
                return StatusWord(PpStatus.Loading)!;
            }

            var word = StatusWord(info.Status);
            if (word != null)
            {
                return word;
            }

            var decimals = format.Decimals;
            var prefix = info.IsApproximate ? PpAvailability.ApproximatePrefix : string.Empty;
            var suffix = info.Status == PpStatus.Qualified
                ? PpAvailability.QualifiedSuffix
                : info.Status == PpStatus.InEvent ? PpAvailability.InEventSuffix : string.Empty;

            var hasPp = HasNumber(info.Pp);
            var text = hasPp ? prefix + NumberFormatting.Fixed(info.Pp!.Value, decimals) : NoData;
            var hasNumber = hasPp;
            if (format.ShowMaxPp)
            {
                text += " / " + FixedOrNoData(info.MaxPp, decimals);
                hasNumber |= HasNumber(info.MaxPp);
            }

            text += (hasNumber ? format.Unit : string.Empty) + suffix;

            if (format.ShowFcPp)
            {
                text += "\nFC " + (HasNumber(info.FcPp) ? prefix + NumberFormatting.Fixed(info.FcPp!.Value, decimals) + format.Unit : NoData);
            }

            if (showComponents)
            {
                text += "\nAcc " + FixedOrNoData(info.AccPp, decimals)
                    + " | Pass " + FixedOrNoData(info.PassPp, decimals)
                    + " | Tech " + FixedOrNoData(info.TechPp, decimals);
            }

            if (format.ShowStars)
            {
                text += "\nStars " + FixedOrNoData(info.Stars, 2);
            }

            return text;
        }

        private static bool IsDisabled(LeaderboardPpInfo? info) => info != null && info.Status == PpStatus.Disabled;

        private static bool HasNumber(double? value) => value.HasValue && NumberFormatting.IsFinite(value.Value);

        private readonly struct LineFormat
        {
            public LineFormat(int decimals, string unit, bool showMaxPp, bool showFcPp, bool showStars)
            {
                Decimals = decimals;
                Unit = unit;
                ShowMaxPp = showMaxPp;
                ShowFcPp = showFcPp;
                ShowStars = showStars;
            }

            public int Decimals { get; }

            public string Unit { get; }

            public bool ShowMaxPp { get; }

            public bool ShowFcPp { get; }

            public bool ShowStars { get; }
        }
    }
}
