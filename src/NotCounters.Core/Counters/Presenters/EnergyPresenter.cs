using System.Text;
using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class EnergyPresenter : CounterPresenter
    {
        private const float LowEnergy = 0.3f;

        private const int MaxIcons = 20;

        private const string DefaultFullIcon = "●";
        private const string DefaultEmptyIcon = "○";

        public EnergyPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.Energy, "Energy", CounterCategory.Health, "Energy")
            {
                Description = "Your energy in percent, or your remaining lives with the 4 Lives / 1 Life modifiers.",
                UsesDecimals = true,
                DefaultDecimals = 0,
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.LivesDisplay,
                        "Show lives as",
                        CounterChoices.LivesText,
                        "Used with the 4 Lives and 1 Life modifiers.",
                        OptionBuilder.Item(CounterChoices.LivesText, "Text (3/4)"),
                        OptionBuilder.Item(CounterChoices.LivesIcons, "Icons")),
                    OptionBuilder.Text(CounterOptionKeys.LifeIconFull, "Life icon", DefaultFullIcon, "Character drawn for each remaining life in icon mode. Shows as a box? Type another character."),
                    OptionBuilder.Text(CounterOptionKeys.LifeIconEmpty, "Lost life icon", DefaultEmptyIcon, "Character drawn for each lost life in icon mode. Shows as a box? Type another character."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            if (stats.IsBatteryEnergy)
            {
                return RenderLives(stats, settings);
            }

            var energy = stats.Energy;
            if (!NumberFormatting.IsFinite(energy))
            {
                return new CounterRender(NoData);
            }

            var danger = stats.EnergyReachedZero || energy < LowEnergy;
            return new CounterRender(NumberFormatting.Percent(energy, Decimals(settings)), valueColor: danger ? CounterColors.Danger : (ColorValue?)null);
        }

        private static CounterRender RenderLives(StatsSnapshot stats, CounterSettings settings)
        {
            var lives = stats.BatteryLives < 0 ? 0 : stats.BatteryLives;
            var maxLives = stats.BatteryMaxLives > lives ? stats.BatteryMaxLives : lives;
            var danger = stats.EnergyReachedZero || (lives <= 1 && maxLives > 1);
            var color = danger ? CounterColors.Danger : (ColorValue?)null;

            if (CounterOptions.GetChoice(settings, CounterOptionKeys.LivesDisplay) != CounterChoices.LivesIcons)
            {
                var text = maxLives > 0 ? Count(lives) + "/" + Count(maxLives) : Count(lives);
                return new CounterRender(text, valueColor: color);
            }

            if (maxLives == 0)
            {
                return new CounterRender(NoData, valueColor: color);
            }

            var full = CounterOptions.GetString(settings, CounterOptionKeys.LifeIconFull);
            var empty = CounterOptions.GetString(settings, CounterOptionKeys.LifeIconEmpty);
            var drawnMax = maxLives > MaxIcons ? MaxIcons : maxLives;
            var drawnFull = lives > drawnMax ? drawnMax : lives;
            var builder = new StringBuilder(drawnMax * 2);
            for (var i = 0; i < drawnMax; i++)
            {
                builder.Append(i < drawnFull ? full : empty);
            }

            return new CounterRender(builder.ToString(), valueColor: color);
        }
    }
}
