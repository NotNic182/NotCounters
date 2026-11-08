using System.Globalization;
using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal abstract class CounterPresenter : ICounterPresenter
    {
        public const string NoData = NumberFormatting.NoValue;

        protected CounterPresenter(CounterTypeDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public CounterTypeDescriptor Descriptor { get; }

        public CounterRender Render(StatsSnapshot stats, CounterSettings settings)
        {
            if (stats == null || settings == null)
            {
                return new CounterRender(NoData);
            }

            return RenderCore(stats, settings);
        }

        protected abstract CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings);

        protected int Decimals(CounterSettings settings) =>
            NumberFormatting.ClampDecimals(settings.Style?.Decimals ?? Descriptor.DefaultDecimals);

        protected static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

        protected static double? Average(double sum, int count) => count > 0 ? sum / count : (double?)null;

        protected static string FixedOrNoData(double? value, int decimals) =>
            value.HasValue ? NumberFormatting.Fixed(value.Value, decimals) : NoData;

        protected static string PercentOrNoData(double? fraction, int decimals) =>
            fraction.HasValue ? NumberFormatting.Percent(fraction.Value, decimals) : NoData;

        protected static ColorValue? RankColor(double? fraction) =>
            fraction.HasValue && NumberFormatting.IsFinite(fraction.Value) ? CounterColors.ForAccuracy(fraction.Value) : (ColorValue?)null;
    }
}
