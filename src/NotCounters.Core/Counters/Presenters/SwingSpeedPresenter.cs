using System.Collections.Generic;
using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class SwingSpeedPresenter : CounterPresenter
    {
        private const int RecentCapacity = 32;

        public SwingSpeedPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.SwingSpeed, "Swing speed", CounterCategory.Swing, "Swing Speed")
            {
                Description = "How fast your saber moves when it cuts a note, in metres per second.",
                UsesDecimals = true,
                DefaultDecimals = 1,
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Mode,
                        "Average over",
                        CounterChoices.SpeedAverage,
                        "Average over the whole song, or only over your most recent cuts.",
                        OptionBuilder.Item(CounterChoices.SpeedAverage, "Whole song"),
                        OptionBuilder.Item(CounterChoices.SpeedRecent, "Recent cuts")),
                    OptionBuilder.Int(CounterOptionKeys.RecentCount, "Number of recent cuts", 10, 1, RecentCapacity, 1, "How many of your latest cuts per hand are averaged when \"Average over\" is Recent cuts."),
                    OptionBuilder.PerHand("Show the left and right saber separately."),
                    OptionBuilder.SeparateLines(),
                    OptionBuilder.HandLetters(),
                    OptionBuilder.SaberColors(),
                    OptionBuilder.ShowUnit("m/s"),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var left = stats.Left;
            var right = stats.Right;
            var decimals = Decimals(settings);
            var unit = CounterOptions.GetBool(settings, CounterOptionKeys.ShowUnit) ? " m/s" : string.Empty;
            var recent = CounterOptions.GetChoice(settings, CounterOptionKeys.Mode) == CounterChoices.SpeedRecent;
            var window = CounterOptions.GetInt(settings, CounterOptionKeys.RecentCount);

            double leftSum, rightSum;
            int leftCount, rightCount;
            if (recent)
            {
                leftCount = SumRecent(left.RecentSaberSpeeds, window, out leftSum);
                rightCount = SumRecent(right.RecentSaberSpeeds, window, out rightSum);
            }
            else
            {
                leftSum = left.SaberSpeedSum;
                leftCount = left.SaberSpeedCount;
                rightSum = right.SaberSpeedSum;
                rightCount = right.SaberSpeedCount;
            }

            if (CounterOptions.GetBool(settings, CounterOptionKeys.PerHand))
            {
                var look = HandLook.For(stats, settings);
                return new CounterRender(
                    look.Join(
                        Entry(leftSum, leftCount, decimals, unit),
                        Entry(rightSum, rightCount, decimals, unit),
                        CounterOptions.GetBool(settings, CounterOptionKeys.SeparateLines)),
                    richText: look.Colored);
            }

            return new CounterRender(Entry(leftSum + rightSum, leftCount + rightCount, decimals, unit));
        }

        private static int SumRecent(List<float> speeds, int window, out double sum)
        {
            sum = 0d;
            if (speeds == null)
            {
                return 0;
            }

            var count = speeds.Count < window ? speeds.Count : window;
            for (var i = speeds.Count - count; i < speeds.Count; i++)
            {
                sum += speeds[i];
            }

            return count;
        }

        private static string Entry(double sum, int count, int decimals, string unit)
        {
            var text = FixedOrNoData(Average(sum, count), decimals);
            return text == NoData ? text : text + unit;
        }
    }
}
