using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class TimingDeviationPresenter : CounterPresenter
    {
        private const double MillisecondsPerSecond = 1000d;

        public TimingDeviationPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.TimingDeviation, "Timing deviation", CounterCategory.Swing, "Timing")
            {
                Description = "How early (-) or late (+) you cut notes on average, in milliseconds.",
                UsesDecimals = true,
                DefaultDecimals = 1,
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Mode,
                        "Mode",
                        CounterChoices.TimingSigned,
                        "Signed: early (-) or late (+) on average. Absolute: how far off, either way.",
                        OptionBuilder.Item(CounterChoices.TimingSigned, "Signed (early/late)"),
                        OptionBuilder.Item(CounterChoices.TimingAbsolute, "Absolute")),
                    OptionBuilder.PerHand("Show the left and right saber separately."),
                    OptionBuilder.SeparateLines(),
                    OptionBuilder.HandLetters(),
                    OptionBuilder.SaberColors(),
                    OptionBuilder.ShowUnit("ms"),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var left = stats.Left;
            var right = stats.Right;
            var decimals = Decimals(settings);
            var signed = CounterOptions.GetChoice(settings, CounterOptionKeys.Mode) != CounterChoices.TimingAbsolute;
            var unit = CounterOptions.GetBool(settings, CounterOptionKeys.ShowUnit) ? " ms" : string.Empty;

            if (CounterOptions.GetBool(settings, CounterOptionKeys.PerHand))
            {
                var look = HandLook.For(stats, settings);
                return new CounterRender(
                    look.Join(
                        Entry(signed ? left.TimingDeviationSum : left.TimingDeviationAbsSum, left.TimingCount, signed, decimals, unit),
                        Entry(signed ? right.TimingDeviationSum : right.TimingDeviationAbsSum, right.TimingCount, signed, decimals, unit),
                        CounterOptions.GetBool(settings, CounterOptionKeys.SeparateLines)),
                    richText: look.Colored);
            }

            var sum = signed
                ? left.TimingDeviationSum + right.TimingDeviationSum
                : left.TimingDeviationAbsSum + right.TimingDeviationAbsSum;
            return new CounterRender(Entry(sum, left.TimingCount + right.TimingCount, signed, decimals, unit));
        }

        private static string Entry(double sumSeconds, int count, bool signed, int decimals, string unit)
        {
            var mean = Average(sumSeconds, count);
            if (!mean.HasValue || !NumberFormatting.IsFinite(mean.Value))
            {
                return NoData;
            }

            var milliseconds = mean.Value * MillisecondsPerSecond;
            return (signed ? NumberFormatting.Signed(milliseconds, decimals) : NumberFormatting.Fixed(milliseconds, decimals)) + unit;
        }
    }
}
