using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class BestPossibleAccuracyPresenter : CounterPresenter
    {
        public BestPossibleAccuracyPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.BestPossibleAccuracy, "Best possible accuracy", CounterCategory.Accuracy, "Best Possible")
            {
                Description = "The best final accuracy you can still reach if you cut every remaining note perfectly.",
                UsesDecimals = true,
                DefaultDecimals = 2,
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var best = stats.BestPossibleAccuracy;
            return new CounterRender(PercentOrNoData(best, Decimals(settings)), valueColor: RankColor(best));
        }
    }
}
