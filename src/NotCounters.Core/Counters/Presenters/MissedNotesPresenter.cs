using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class MissedNotesPresenter : CounterPresenter
    {
        public MissedNotesPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.MissedNotes, "Missed notes", CounterCategory.Notes, "Misses")
            {
                Description = "Notes you missed or cut badly.",
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Mode,
                        "Count",
                        CounterChoices.MissCombined,
                        "Misses: notes that passed uncut. Bad cuts: wrong color or direction, too slow or too soon.",
                        OptionBuilder.Item(CounterChoices.MissCombined, "Misses + bad cuts"),
                        OptionBuilder.Item(CounterChoices.MissMisses, "Misses only"),
                        OptionBuilder.Item(CounterChoices.MissBadCuts, "Bad cuts only"),
                        OptionBuilder.Item(CounterChoices.MissSeparate, "Separate lines")),
                    OptionBuilder.Bool(CounterOptionKeys.IncludeChainLinks, "Count chain links", false, "Off: only a chain's head counts. On: its links count too (not with \"Per hand\")."),
                    OptionBuilder.Bool(CounterOptionKeys.IncludeBombs, "Count bomb hits", false, "Added to the count, or shown as \"Bombs\" with Separate lines or with \"Per hand\" on."),
                    OptionBuilder.PerHand("Split the count into left and right saber."),
                    OptionBuilder.SeparateLines(),
                    OptionBuilder.HandLetters(),
                    OptionBuilder.SaberColors(),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var mode = CounterOptions.GetChoice(settings, CounterOptionKeys.Mode);
            var includeBombs = CounterOptions.GetBool(settings, CounterOptionKeys.IncludeBombs);
            var bombs = includeBombs ? stats.BombsHit : 0;

            if (CounterOptions.GetBool(settings, CounterOptionKeys.PerHand))
            {
                var look = HandLook.For(stats, settings);
                return new CounterRender(RenderPerHand(stats, settings, mode, includeBombs, look), richText: look.Colored);
            }

            var chainLinks = CounterOptions.GetBool(settings, CounterOptionKeys.IncludeChainLinks);
            var misses = stats.Misses + (chainLinks ? stats.ChainLinkMisses : 0);
            var badCuts = stats.BadCuts + (chainLinks ? stats.ChainLinkBadCuts : 0);

            switch (mode)
            {
                case CounterChoices.MissMisses:
                    return new CounterRender(Count(misses + bombs));
                case CounterChoices.MissBadCuts:
                    return new CounterRender(Count(badCuts + bombs));
                case CounterChoices.MissSeparate:
                    var lines = "Missed " + Count(misses) + "\nBad cuts " + Count(badCuts);
                    return new CounterRender(includeBombs ? lines + "\nBombs " + Count(bombs) : lines);
                default:
                    return new CounterRender(Count(misses + badCuts + bombs));
            }
        }

        private static string RenderPerHand(StatsSnapshot stats, CounterSettings settings, string mode, bool includeBombs, HandLook look)
        {
            var left = stats.Left;
            var right = stats.Right;
            var separateLines = CounterOptions.GetBool(settings, CounterOptionKeys.SeparateLines);
            var bombsSuffix = includeBombs ? (separateLines ? "\n" : " | ") + "Bombs " + Count(stats.BombsHit) : string.Empty;

            switch (mode)
            {
                case CounterChoices.MissMisses:
                    return look.Join(Count(left.Misses), Count(right.Misses), separateLines) + bombsSuffix;
                case CounterChoices.MissBadCuts:
                    return look.Join(Count(left.BadCuts), Count(right.BadCuts), separateLines) + bombsSuffix;
                case CounterChoices.MissSeparate:
                    var lines = "Missed " + look.Join(Count(left.Misses), Count(right.Misses), false)
                        + "\nBad cuts " + look.Join(Count(left.BadCuts), Count(right.BadCuts), false);
                    return includeBombs ? lines + "\nBombs " + Count(stats.BombsHit) : lines;
                default:
                    return look.Join(Count(left.Misses + left.BadCuts), Count(right.Misses + right.BadCuts), separateLines) + bombsSuffix;
            }
        }
    }
}
