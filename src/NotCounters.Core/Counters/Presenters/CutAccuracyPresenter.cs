using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class CutAccuracyPresenter : CounterPresenter
    {
        private const double MaxCutScore = 115d;
        private const double MaxPreSwing = 70d;
        private const double MaxPostSwing = 30d;
        private const double MaxCenter = 15d;

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
                    OptionBuilder.HandLetters(),
                    OptionBuilder.HandColors(),
                    OptionBuilder.LeftColor(),
                    OptionBuilder.RightColor(),
                    OptionBuilder.Bool(CounterOptionKeys.ShowBreakdown, "Show breakdown", false, "Append the average pre-swing (max 70), post-swing (max 30) and center (max 15) scores."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowPercent, "Show as percent", false, "Show the average cut as a percent of 115, and the breakdown as a percent of 70, 30 and 15."),
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
            var percent = CounterOptions.GetBool(settings, CounterOptionKeys.ShowPercent);

            var color = RankColor(Average(both.CutSum, both.CutCount) / MaxCutScore);
            if (!CounterOptions.GetBool(settings, CounterOptionKeys.PerHand))
            {
                return new CounterRender(Entry(both, decimals, breakdown, percent), valueColor: color);
            }

            var look = HandLook.For(stats, settings);
            var text = look.Join(Entry(left, decimals, breakdown, percent), Entry(right, decimals, breakdown, percent), CounterOptions.GetBool(settings, CounterOptionKeys.SeparateLines));
            return new CounterRender(text, valueColor: color, richText: look.Colored);
        }

        private static string Entry(CutSums sums, int decimals, bool breakdown, bool percent)
        {
            var cut = Average(sums.CutSum, sums.CutCount);
            var average = percent ? PercentOrNoData(cut / MaxCutScore, decimals) : FixedOrNoData(cut, decimals);
            if (!breakdown || sums.CutCount <= 0)
            {
                return average;
            }

            return average + " ("
                + Part(Average(sums.PreSum, sums.PreCount), MaxPreSwing, decimals, percent) + "/"
                + Part(Average(sums.PostSum, sums.PostCount), MaxPostSwing, decimals, percent) + "/"
                + Part(Average(sums.CenterSum, sums.CenterCount), MaxCenter, decimals, percent) + ")";
        }

        private static string Part(double? average, double max, int decimals, bool percent) =>
            FixedOrNoData(percent ? average / max * 100d : average, decimals);

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
