using System;
using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class FailIndicatorPresenter : CounterPresenter
    {
        public const string TimePlaceholder = "{time}";

        private const string DefaultText = "Failed at " + TimePlaceholder;

        public FailIndicatorPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.FailIndicator, "Fail indicator", CounterCategory.Health, "No Fail")
            {
                Description = "Appears when your energy runs out while No Fail keeps the song going, and shows when it happened.",
                Options = new[]
                {
                    OptionBuilder.Text(CounterOptionKeys.Text, "Text", DefaultText, "Message shown after the fail. " + TimePlaceholder + " is replaced by the song time of the fail. Empty uses \"" + DefaultText + "\"."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            if (!stats.EnergyReachedZero)
            {
                return CounterRender.Empty;
            }

            var template = CounterOptions.GetString(settings, CounterOptionKeys.Text);
            if (string.IsNullOrWhiteSpace(template))
            {
                template = DefaultText;
            }

            var text = template.IndexOf(TimePlaceholder, StringComparison.Ordinal) >= 0
                ? template.Replace(TimePlaceholder, TimeFormatting.MinutesSeconds(stats.FailedAtSongTime ?? stats.SongTime))
                : template;
            return new CounterRender(text, valueColor: CounterColors.Danger);
        }
    }
}
