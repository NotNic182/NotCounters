using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class ComboPresenter : CounterPresenter
    {
        public ComboPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.Combo, "Combo", CounterCategory.Score, "Combo")
            {
                Description = "Your current combo.",
                Options = new[]
                {
                    OptionBuilder.Bool(CounterOptionKeys.ShowMaxCombo, "Show max combo", false, "Append your best combo of this play: \"123 (max 240)\"."),
                    OptionBuilder.Bool(CounterOptionKeys.ShowFcMarker, "Show FC marker", true, "Append \"FC\" while you still have a full combo."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var text = Count(stats.Combo);
            if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowFcMarker) && stats.IsFullCombo && stats.Combo > 0)
            {
                text += " FC";
            }

            if (CounterOptions.GetBool(settings, CounterOptionKeys.ShowMaxCombo))
            {
                text += " (max " + Count(stats.MaxCombo) + ")";
            }

            return new CounterRender(text);
        }
    }
}
