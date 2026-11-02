using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class CutAccuracyPresenter : CounterPresenter
    {
        private const double MaxCutScore = 115d;

        public CutAccuracyPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.CutAccuracy, "Average cut", CounterCategory.Accuracy, "Avg Cut")
            {
                Description = "Your average cut score out of 115.",
                UsesDecimals = true,
                DefaultDecimals = 2,
                Options = new[]
                {
                    OptionBuilder.PerHand("Show the left and right saber averages separately."),
                    OptionBuilder.SeparateLines(),
                    OptionBuilder.Bool(CounterOptionKeys.ShowBreakdown, "Show breakdown", false, "Append the average pre-swing (max 70), post-swing (max 30) and center (max 15) scores."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var left = CutSums.Of(stats.Left);
            var right = CutSums.Of(stats.Right);
            var both = left + right;
            var decimals = Decimals(settings);
            var breakdown = CounterOptions.GetBool(settings, CounterOptionKeys.ShowBreakdown);

            var text = CounterOptions.GetBool(settings, CounterOptionKeys.PerHand)
                ? Hands(Entry(left, decimals, breakdown), Entry(right, decimals, breakdown), CounterOptions.GetBool(settings, CounterOptionKeys.SeparateLines))
                : Entry(both, decimals, breakdown);

            return new CounterRender(text, valueColor: RankColor(Average(both.CutSum, both.CutCount) / MaxCutScore));
        }

        private static string Entry(CutSums sums, int decimals, bool breakdown)
        {
            var average = FixedOrNoData(Average(sums.CutSum, sums.CutCount), decimals);
            if (!breakdown || sums.CutCount <= 0)
            {
                return average;
            }

            return average + " ("
                + FixedOrNoData(Average(sums.PreSum, sums.PreCount), decimals) + "/"
                + FixedOrNoData(Average(sums.PostSum, sums.PostCount), decimals) + "/"
                + FixedOrNoData(Average(sums.CenterSum, sums.CenterCount), decimals) + ")";
        }

        private readonly struct CutSums
        {
            private CutSums(long cutSum, int cutCount, long preSum, int preCount, long postSum, int postCount, long centerSum, int centerCount)
            {
                CutSum = cutSum;
                CutCount = cutCount;
                PreSum = preSum;
                PreCount = preCount;
                PostSum = postSum;
                PostCount = postCount;
                CenterSum = centerSum;
                CenterCount = centerCount;
            }

            public long CutSum { get; }

            public int CutCount { get; }

            public long PreSum { get; }

            public int PreCount { get; }

            public long PostSum { get; }

            public int PostCount { get; }

            public long CenterSum { get; }

            public int CenterCount { get; }

            public static CutSums Of(HandStats hand) => new CutSums(
                hand.CutScoreSum,
                hand.CutScoreCount,
                hand.PreSwingSum,
                hand.PreSwingCount,
                hand.PostSwingSum,
                hand.PostSwingCount,
                hand.CenterSum,
                hand.CenterCount);

            public static CutSums operator +(CutSums a, CutSums b) => new CutSums(
                a.CutSum + b.CutSum,
                a.CutCount + b.CutCount,
                a.PreSum + b.PreSum,
                a.PreCount + b.PreCount,
                a.PostSum + b.PostSum,
                a.PostCount + b.PostCount,
                a.CenterSum + b.CenterSum,
                a.CenterCount + b.CenterCount);
        }
    }
}
