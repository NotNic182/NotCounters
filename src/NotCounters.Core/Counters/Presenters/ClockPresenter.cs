using System;
using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class ClockPresenter : CounterPresenter
    {
        public ClockPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.Clock, "Clock", CounterCategory.Time, "Clock")
            {
                Description = "The current time of day.",
                Options = new[]
                {
                    OptionBuilder.Bool(CounterOptionKeys.Use24Hour, "24-hour clock", true, "Show 14:05 instead of 2:05 PM."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowSeconds, "Show seconds", false, "Show seconds as well."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            DateTime now;
            try
            {
                now = Clock.Now();
            }
            catch (Exception)
            {
                return new CounterRender(TimeFormatting.NoTime);
            }

            return new CounterRender(TimeFormatting.Clock(
                now,
                CounterOptions.GetBool(settings, CounterOptionKeys.Use24Hour),
                CounterOptions.GetBool(settings, CounterOptionKeys.ShowSeconds)));
        }
    }
}
