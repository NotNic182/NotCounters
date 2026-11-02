using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Scoring;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class MultiplierPresenter : CounterPresenter
    {
        public MultiplierPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.Multiplier, "Multiplier", CounterCategory.Score, "Multiplier")
            {
                Description = "Your current combo multiplier (x1, x2, x4 or x8), like the vanilla multiplier ring.",
                Options = new[]
                {
                    OptionBuilder.Bool(CounterOptionKeys.ShowProgress, "Show progress", false, "Add how far you are towards the next multiplier."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var multiplier = stats.Multiplier;
            var text = "x" + Count(multiplier);
            if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowProgress) && multiplier < MultiplierState.MaxMultiplier)
            {
                text += " " + NumberFormatting.Percent(stats.MultiplierProgress, 0);
            }

            return new CounterRender(text);
        }
    }
}
