using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class BombsHitPresenter : CounterPresenter
    {
        public BombsHitPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.BombsHit, "Bombs hit", CounterCategory.Notes, "Bombs")
            {
                Description = "How many bombs you hit.",
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings) => new CounterRender(Count(stats.BombsHit));
    }
}
