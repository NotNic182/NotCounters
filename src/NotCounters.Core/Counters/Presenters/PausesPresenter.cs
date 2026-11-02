using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class PausesPresenter : CounterPresenter
    {
        public PausesPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.Pauses, "Pauses", CounterCategory.Info, "Pauses")
            {
                Description = "How many times you paused this play.",
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings) => new CounterRender(Count(stats.Pauses));
    }
}
