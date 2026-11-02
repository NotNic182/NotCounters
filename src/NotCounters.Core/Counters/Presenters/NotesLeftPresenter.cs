using NotCounters.Core.Config;
using NotCounters.Core.Stats;

namespace NotCounters.Core.Counters.Presenters
{
    internal sealed class NotesLeftPresenter : CounterPresenter
    {
        public NotesLeftPresenter()
            : base(new CounterTypeDescriptor(CounterTypeIds.NotesLeft, "Notes left", CounterCategory.Notes, "Notes Left")
            {
                Description = "How many notes are left in the map.",
                Options = new[]
                {
                    OptionBuilder.Choice(
                        CounterOptionKeys.Format,
                        "Format",
                        CounterChoices.NotesLeftLeft,
                        "Notes left, notes left out of the total, or notes hit (good cuts) out of the total.",
                        OptionBuilder.Item(CounterChoices.NotesLeftLeft, "Left"),
                        OptionBuilder.Item(CounterChoices.NotesLeftLeftTotal, "Left / total"),
                        OptionBuilder.Item(CounterChoices.NotesLeftHitTotal, "Hit / total")),
                    OptionBuilder.Bool(CounterOptionKeys.IncludeChainLinks, "Count chain links", false, "Off: only a chain's head counts. On: its links count too."),
                    OptionBuilder.Bool(CounterOptionKeys.IncludeBombs, "Include bombs", false, "Count bombs as notes. For hit / total, a bomb you avoided counts as hit."),
                },
            })
        {
        }

        protected override CounterRender RenderCore(StatsSnapshot stats, CounterSettings settings)
        {
            var chainLinks = CounterOptions.GetBool(settings, CounterOptionKeys.IncludeChainLinks);
            var bombs = CounterOptions.GetBool(settings, CounterOptionKeys.IncludeBombs);
            var total = stats.TotalNotes + (chainLinks ? stats.TotalChainLinks : 0) + (bombs ? stats.TotalBombs : 0);
            if (total <= 0)
            {
                return new CounterRender(NoData);
            }

            var resolved = stats.ResolvedNotes + (chainLinks ? stats.ResolvedChainLinks : 0) + (bombs ? stats.ResolvedBombs : 0);
            var left = total - resolved;
            if (left < 0)
            {
                left = 0;
            }

            switch (CounterOptions.GetChoice(settings, CounterOptionKeys.Format))
            {
                case CounterChoices.NotesLeftLeftTotal:
                    return new CounterRender(Count(left) + " / " + Count(total));
                case CounterChoices.NotesLeftHitTotal:
                    var hit = stats.GoodCuts + (chainLinks ? stats.ChainLinkGoodCuts : 0) + (bombs ? AvoidedBombs(stats) : 0);
                    return new CounterRender(Count(hit) + " / " + Count(total));
                default:
                    return new CounterRender(Count(left));
            }
        }

        private static int AvoidedBombs(StatsSnapshot stats)
        {
            var avoided = stats.ResolvedBombs - stats.BombsHit;
            return avoided > 0 ? avoided : 0;
        }
    }
}
