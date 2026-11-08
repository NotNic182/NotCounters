using System;
using System.Collections.Generic;
using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class PersonalBestPresenter : CounterPresenter
    {
        private const string NoPersonalBest = PersonalBestStatus.NoPersonalBest;
        private const string PracticeText = PersonalBestStatus.Practice;

        public PersonalBestPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.PersonalBest, "Personal best", CounterCategory.Score, "PB")
            {
                Description = "Your personal best on this difficulty and how your current accuracy compares to it.",
                UsesDecimals = true,
                DefaultDecimals = 2,
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Source,
                        "Source",
                        CounterChoices.PbSourceAuto,
                        "Automatic: your BeatLeader PB, else your ScoreSaber PB, else the one this game stored.",
                        OptionBuilder.Item(CounterChoices.PbSourceAuto, "Automatic"),
                        OptionBuilder.Item(CounterChoices.PbSourceLocal, "Local (this game)"),
                        OptionBuilder.Item(CounterChoices.PbSourceBeatLeader, "BeatLeader"),
                        OptionBuilder.Item(CounterChoices.PbSourceScoreSaber, "ScoreSaber")),
                    OptionBuilder.Bool(CounterOptionKeys.ShowAccuracy, "Show PB accuracy", true, "Show your PB's accuracy. With PB over time on, turn this and \"Show PB score\" off to see only the difference."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowScore, "Show PB score", false, "Show the score of your personal best."),
                    OptionBuilder.Grouping(CounterChoices.GroupingSpace),
                    OptionBuilder.Bool(CounterOptionKeys.ShowDelta, "Show difference", true, "Show under your PB how far your current accuracy is above (+) or below (-) it."),
                    OptionBuilder.Bool(CounterOptionKeys.AtThisPoint, "PB over time", false, "Compare with your PB at the same point in the song. Needs a BeatLeader PB, or a PB set while this is on."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var candidates = CounterChoices.PbSourceCandidates(CounterOptions.GetChoice(settings, CounterOptionKeys.Source));
            var automatic = candidates.Count > 1;
            var showDelta = CounterOptions.GetBool(settings, CounterOptionKeys.ShowDelta);

            var source = candidates[0];
            if (stats.IsPractice || stats.IsPartyOrCampaign)
            {
                var label = automatic ? null : LabelFor(source);
                return new CounterRender(stats.IsPractice ? PracticeText : NoPersonalBest, label, reservesDetail: showDelta);
            }

            var pb = Resolve(stats, candidates, out source);
            if (pb == null || !pb.Available)
            {
                var status = pb?.StatusText;
                return new CounterRender(string.IsNullOrWhiteSpace(status) ? NoPersonalBest : status!, automatic ? null : LabelFor(source), reservesDetail: showDelta);
            }

            var labelOverride = LabelFor(source);

            var decimals = Decimals(settings);
            var showAccuracy = CounterOptions.GetBool(settings, CounterOptionKeys.ShowAccuracy);
            var showScore = CounterOptions.GetBool(settings, CounterOptionKeys.ShowScore);
            var overTime = CounterOptions.GetBool(settings, CounterOptionKeys.AtThisPoint);
            var differenceOnly = overTime && showDelta && !showAccuracy && !showScore;
            if (!showAccuracy && !showScore)
            {
                showAccuracy = true;
            }

            var current = source == PbSource.Local ? stats.ModifiedRelativeScore : stats.Accuracy;
            var playing = current.HasValue && NumberFormatting.IsFinite(current.Value);
            double? pbRaw = NumberFormatting.IsFinite(pb.Accuracy) ? pb.Accuracy : (double?)null;
            if (playing && pb.Line != null && overTime)
            {
                pbRaw = pb.Line.At(stats.SongTime) ?? pbRaw;
            }

            double? points = playing && pbRaw.HasValue ? (current.GetValueOrDefault() - pbRaw.Value) * 100d : (double?)null;
            if (differenceOnly)
            {
                return points.HasValue
                    ? new CounterRender(NumberFormatting.SignedPercentPoints(points.Value, decimals), labelOverride, DeltaColor(points.Value, decimals))
                    : new CounterRender(NoData, labelOverride);
            }

            double? pbAccuracy = pbRaw.HasValue ? Clamp01(pbRaw.Value) : (double?)null;
            var text = showAccuracy ? PercentOrNoData(pbAccuracy, decimals) : string.Empty;
            if (showScore)
            {
                var grouping = CounterChoices.ToNumberGrouping(CounterOptions.GetChoice(settings, CounterOptionKeys.Grouping));
                var score = NumberFormatting.Integer(pb.Score, grouping);
                text = showAccuracy ? text + " | " + score : score;
            }

            var color = RankColor(pbAccuracy);
            if (showDelta && points.HasValue)
            {
                var delta = NumberFormatting.SignedPercentPoints(points.Value, decimals);
                return new CounterRender(text, labelOverride, color, detail: delta, detailColor: DeltaColor(points.Value, decimals), reservesDetail: true);
            }

            return new CounterRender(text, labelOverride, color, reservesDetail: showDelta);
        }

        private static ColorValue DeltaColor(double points, int decimals) =>
            CounterColors.ForDelta(NumberFormatting.RoundForDisplay(points, decimals));

        private static PersonalBestInfo? Resolve(StatsSnapshot stats, IReadOnlyList<PbSource> candidates, out PbSource source)
        {
            PersonalBestInfo? entry = null;
            source = candidates[candidates.Count - 1];
            foreach (var candidate in candidates)
            {
                entry = stats.GetPersonalBest(candidate);
                source = candidate;
                if (entry != null && (entry.Available || PersonalBestStatus.IsPending(entry)))
                {
                    return entry;
                }
            }

            return entry;
        }

        private static string? LabelFor(PbSource source) =>
            source == PbSource.BeatLeader ? "PB (BL)" : source == PbSource.ScoreSaber ? "PB (SS)" : null;

        private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;
    }
}
