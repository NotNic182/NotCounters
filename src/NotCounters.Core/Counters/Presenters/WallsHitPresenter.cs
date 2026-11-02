using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class WallsHitPresenter : CounterPresenter
    {
        public WallsHitPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.WallsHit, "Walls hit", CounterCategory.Notes, "Walls")
            {
                Description = "How many times your head went into a wall.",
                UsesDecimals = true,
                DefaultDecimals = 1,
                Options = new[]
                {
                    OptionBuilder.Bool(CounterOptionKeys.ShowTime, "Show time in walls", false, "Append the total seconds your head spent inside walls. Decimals apply to the seconds."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var text = Count(stats.WallsHit);
            if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowTime))
            {
                text += " (" + NumberFormatting.Fixed(stats.SecondsInWalls, Decimals(settings)) + " s)";
            }

            return new CounterRender(text);
        }
    }
}
