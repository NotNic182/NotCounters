using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class SwingQualityPresenter : CounterPresenter
    {
        public SwingQualityPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.SwingQuality, "Swing quality", CounterCategory.Swing, "Full Swings (Pre/Post)")
            {
                Description = "How often you get the full 70 pre-swing and 30 post-swing points, and how many swings fell short.",
                UsesDecimals = true,
                DefaultDecimals = 1,
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Display,
                        "Show",
                        CounterChoices.SwingPercent,
                        "Percentage of full swings, number of underswings, or both.",
                        OptionBuilder.Item(CounterChoices.SwingPercent, "Full swing %"),
                        OptionBuilder.Item(CounterChoices.SwingUnderswings, "Underswing count"),
                        OptionBuilder.Item(CounterChoices.SwingBoth, "Both")),
                    OptionBuilder.PerHand("Show the left and right saber separately."),
                    OptionBuilder.SeparateLines(),
                    OptionBuilder.HandLetters(),
                    OptionBuilder.SaberColors(),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var display = CounterOptions.GetChoice(settings, CounterOptionKeys.Display);
            var decimals = Decimals(settings);
            var left = stats.Left;
            var right = stats.Right;
            var labelOverride = display == CounterChoices.SwingUnderswings ? "Underswings (Pre/Post)" : null;

            string text;
            var richText = false;
            if (CounterOptions.GetBool(settings, CounterOptionKeys.PerHand))
            {
                var look = HandLook.For(stats, settings);
                richText = look.Colored;
                text = look.Join(
                    Pair(left.FullPreSwings, left.PreSwingCount, left.FullPostSwings, left.PostSwingCount, display, decimals),
                    Pair(right.FullPreSwings, right.PreSwingCount, right.FullPostSwings, right.PostSwingCount, display, decimals),
                    CounterOptions.GetBool(settings, CounterOptionKeys.SeparateLines));
            }
            else
            {
                text = Pair(
                    left.FullPreSwings + right.FullPreSwings,
                    left.PreSwingCount + right.PreSwingCount,
                    left.FullPostSwings + right.FullPostSwings,
                    left.PostSwingCount + right.PostSwingCount,
                    display,
                    decimals);
            }

            return new CounterRender(text, labelOverride, richText: richText);
        }

        private static string Pair(int fullPre, int preCount, int fullPost, int postCount, string display, int decimals) =>
            Part(fullPre, preCount, display, decimals) + " / " + Part(fullPost, postCount, display, decimals);

        private static string Part(int full, int count, string display, int decimals)
        {
            if (count <= 0)
            {
                return NoData;
            }

            var under = count - full;
            if (under < 0)
            {
                under = 0;
            }

            switch (display)
            {
                case CounterChoices.SwingUnderswings:
                    return Count(under);
                case CounterChoices.SwingBoth:
                    return NumberFormatting.Percent((double)full / count, decimals) + " (" + Count(under) + ")";
                default:
                    return NumberFormatting.Percent((double)full / count, decimals);
            }
        }
    }
}
