using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class AccuracyPresenter : CounterPresenter
    {
        public AccuracyPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.Accuracy, "Accuracy", CounterCategory.Accuracy, "Accuracy")
            {
                Description = "Your current accuracy, the same number the vanilla HUD shows (modifiers are ignored).",
                UsesDecimals = true,
                DefaultDecimals = 2,
                Options = new[]
                {
                    OptionBuilder.Bool(CounterOptionKeys.ShowRank, "Show rank", false, "Append the vanilla rank letter (SS, S, A, ...)."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var accuracy = stats.Accuracy;
            if (!accuracy.HasValue)
            {
                return new CounterRender(NoData);
            }

            var text = NumberFormatting.Percent(accuracy.Value, Decimals(settings));
            if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowRank) && !string.IsNullOrEmpty(stats.Rank))
            {
                return new CounterRender(text + " " + stats.Rank, valueColor: CounterColors.ForRank(stats.Rank) ?? RankColor(accuracy));
            }

            return new CounterRender(text, valueColor: RankColor(accuracy));
        }
    }
}
