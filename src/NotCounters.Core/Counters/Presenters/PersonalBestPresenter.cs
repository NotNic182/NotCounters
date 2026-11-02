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
                    OptionBuilder.Bool(CounterOptionKeys.ShowAccuracy, "Show PB accuracy", true, "Show the accuracy of your personal best. With \"Show PB score\" also off, it is shown anyway."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowScore, "Show PB score", false, "Show the score of your personal best."),
                    OptionBuilder.Grouping(CounterChoices.GroupingSpace),
                    OptionBuilder.Bool(CounterOptionKeys.ShowDelta, "Show difference", true, "Show under your PB how far your current accuracy is above (+) or below (-) it."),
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
            if (!showAccuracy && !showScore)
            {
                showAccuracy = true;
            }

            double? pbRaw = NumberFormatting.IsFinite(pb.Accuracy) ? pb.Accuracy : (double?)null;
            double? pbAccuracy = pbRaw.HasValue ? Clamp01(pbRaw.Value) : (double?)null;
            var text = showAccuracy ? PercentOrNoData(pbAccuracy, decimals) : string.Empty;
            if (showScore)
            {
                var grouping = CounterChoices.ToNumberGrouping(CounterOptions.GetChoice(settings, CounterOptionKeys.Grouping));
                var score = NumberFormatting.Integer(pb.Score, grouping);
                text = showAccuracy ? text + " | " + score : score;
            }

            var color = RankColor(pbAccuracy);
            var current = source == PbSource.Local ? stats.ModifiedRelativeScore : stats.Accuracy;
            if (showDelta
                && current.HasValue && NumberFormatting.IsFinite(current.Value) && pbRaw.HasValue)
            {
                var points = (current.Value - pbRaw.Value) * 100d;
                var delta = NumberFormatting.SignedPercentPoints(points, decimals);
                var deltaColor = CounterColors.ForDelta(NumberFormatting.RoundForDisplay(points, decimals));
                return new CounterRender(text, labelOverride, color, detail: delta, detailColor: deltaColor, reservesDetail: true);
            }

            return new CounterRender(text, labelOverride, color, reservesDetail: showDelta);
        }

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
