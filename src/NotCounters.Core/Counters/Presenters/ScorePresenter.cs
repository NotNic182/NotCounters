using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class ScorePresenter : CounterPresenter
    {
        public ScorePresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.Score, "Score", CounterCategory.Score, "Score")
            {
                Description = "Your current score.",
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.ScoreType,
                        "Score type",
                        CounterChoices.ScoreModified,
                        "With modifiers: the score the vanilla HUD shows. Without: before the modifier multiplier.",
                        OptionBuilder.Item(CounterChoices.ScoreModified, "With modifiers"),
                        OptionBuilder.Item(CounterChoices.ScoreMultiplied, "Without modifiers")),
                    OptionBuilder.Grouping(CounterChoices.GroupingSpace),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var score = CounterOptions.GetChoice(settings, CounterOptionKeys.ScoreType) == CounterChoices.ScoreMultiplied
                ? stats.MultipliedScore
                : stats.ModifiedScore;
            var grouping = CounterChoices.ToNumberGrouping(CounterOptions.GetChoice(settings, CounterOptionKeys.Grouping));
            return new CounterRender(NumberFormatting.Integer(score, grouping));
        }
    }
}
