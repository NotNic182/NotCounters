using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class FcAccuracyPresenter : CounterPresenter
    {
        public FcAccuracyPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.FcAccuracy, "FC accuracy", CounterCategory.Accuracy, "FC Accuracy")
            {
                Description = "The accuracy you would have with a full combo, at your current cut quality.",
                UsesDecimals = true,
                DefaultDecimals = 2,
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Display,
                        "Show",
                        CounterChoices.FcDisplayValue,
                        "The difference is FC accuracy minus your accuracy: what misses, bad cuts, bombs and walls cost.",
                        OptionBuilder.Item(CounterChoices.FcDisplayValue, "FC accuracy"),
                        OptionBuilder.Item(CounterChoices.FcDisplayValueAndDifference, "FC accuracy + difference"),
                        OptionBuilder.Item(CounterChoices.FcDisplayDifference, "Difference only")),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var fcAccuracy = stats.FcAccuracy;
            if (!fcAccuracy.HasValue)
            {
                return new CounterRender(NoData);
            }

            var decimals = Decimals(settings);
            var display = CounterOptions.GetChoice(settings, CounterOptionKeys.Display);
            var accuracy = stats.Accuracy;

            if (display == CounterChoices.FcDisplayDifference)
            {
                if (!accuracy.HasValue)
                {
                    return new CounterRender(NoData);
                }

                var points = (fcAccuracy.Value - accuracy.Value) * 100d;
                return new CounterRender(
                    NumberFormatting.SignedPercentPoints(points, decimals),
                    valueColor: CounterColors.ForDelta(NumberFormatting.RoundForDisplay(points, decimals)));
            }

            var text = NumberFormatting.Percent(fcAccuracy.Value, decimals);
            if (display == CounterChoices.FcDisplayValueAndDifference && accuracy.HasValue)
            {
                text += " (" + NumberFormatting.SignedPercentPoints((fcAccuracy.Value - accuracy.Value) * 100d, decimals) + ")";
            }

            return new CounterRender(text, valueColor: RankColor(fcAccuracy));
        }
    }
}
