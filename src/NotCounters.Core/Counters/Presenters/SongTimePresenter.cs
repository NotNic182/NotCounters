using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class SongTimePresenter : CounterPresenter
    {
        public SongTimePresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.SongTime, "Song time", CounterCategory.Time, "Time")
            {
                Description = "Where you are in the song.",
                UsesDecimals = true,
                DefaultDecimals = 0,
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Format,
                        "Format",
                        CounterChoices.TimeElapsedTotal,
                        "Which time to show. Decimals apply to the percentage.",
                        OptionBuilder.Item(CounterChoices.TimeElapsed, "Elapsed"),
                        OptionBuilder.Item(CounterChoices.TimeRemaining, "Remaining"),
                        OptionBuilder.Item(CounterChoices.TimeElapsedTotal, "Elapsed / total"),
                        OptionBuilder.Item(CounterChoices.TimeTotal, "Total"),
                        OptionBuilder.Item(CounterChoices.TimePercent, "Percent")),
                    OptionBuilder.Bool(CounterOptionKeys.RealTime, "Real time", true, "On: times follow the real clock at other song speeds. Off: song time, as vanilla shows it."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            double time = stats.SongTime;
            double length = stats.SongLength;
            if (!NumberFormatting.IsFinite(time))
            {
                return new CounterRender(NoData);
            }

            var hasLength = NumberFormatting.IsFinite(length) && length > 0d;
            if (time < 0d)
            {
                time = 0d;
            }
            else if (hasLength && time > length)
            {
                time = length;
            }

            double speed = stats.SongSpeed;
            if (!CounterOptions.GetBool(settings, CounterOptionKeys.RealTime) || !NumberFormatting.IsFinite(speed) || speed <= 0d)
            {
                speed = 1d;
            }

            switch (CounterOptions.GetChoice(settings, CounterOptionKeys.Format))
            {
                case CounterChoices.TimeElapsed:
                    return new CounterRender(TimeFormatting.MinutesSeconds(time / speed));
                case CounterChoices.TimeRemaining:
                    return new CounterRender(hasLength ? TimeFormatting.MinutesSeconds((time - length) / speed) : TimeFormatting.NoTime);
                case CounterChoices.TimeTotal:
                    return new CounterRender(hasLength ? TimeFormatting.MinutesSeconds(length / speed) : TimeFormatting.NoTime);
                case CounterChoices.TimePercent:
                    return new CounterRender(hasLength ? NumberFormatting.Percent(time / length, Decimals(settings)) : NoData);
                default:
                    var total = hasLength ? TimeFormatting.MinutesSeconds(length / speed) : TimeFormatting.NoTime;
                    return new CounterRender(TimeFormatting.MinutesSeconds(time / speed) + " / " + total);
            }
        }
    }
}
