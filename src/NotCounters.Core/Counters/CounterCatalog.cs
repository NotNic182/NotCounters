using System;
using System.Collections.Generic;
using NotCounters.Core.Counters.Presenters;

namespace NotCounters.Core.Counters
{
    public static class CounterCatalog
    {
        private static readonly ICounterPresenter[] Presenters =
        {
            new AccuracyPresenter(),
            new ScorePresenter(),
            new ComboPresenter(),
            new MissedNotesPresenter(),
            new FcAccuracyPresenter(),
            new BestPossibleAccuracyPresenter(),
            new PersonalBestPresenter(),
            new NotesLeftPresenter(),
            new PpPresenter(),
            new CutAccuracyPresenter(),
            new TimingDeviationPresenter(),
            new SwingSpeedPresenter(),
            new SwingQualityPresenter(),
            new EnergyPresenter(),
            new SongTimePresenter(),
            new WallsHitPresenter(),
            new BombsHitPresenter(),
            new PausesPresenter(),
            new MapInfoPresenter(),
            new ClockPresenter(),
            new FailIndicatorPresenter(),
            new MultiplierPresenter(),
        };

        private static readonly Dictionary<string, ICounterPresenter> ByType = BuildTypeIndex();

        private static readonly Dictionary<string, Dictionary<string, CounterOptionDescriptor>> OptionsByType = BuildOptionIndex();

        public static IReadOnlyList<ICounterPresenter> All => Presenters;

        public static ICounterPresenter? Get(string typeId) =>
            typeId != null && ByType.TryGetValue(typeId, out var presenter) ? presenter : null;

        public static CounterTypeDescriptor? GetDescriptor(string typeId) => Get(typeId)?.Descriptor;

        internal static CounterOptionDescriptor? FindOption(string typeId, string key)
        {
            if (typeId == null || key == null || !OptionsByType.TryGetValue(typeId, out var options))
            {
                return null;
            }

            return options.TryGetValue(key, out var option) ? option : null;
        }

        private static Dictionary<string, ICounterPresenter> BuildTypeIndex()
        {
            var index = new Dictionary<string, ICounterPresenter>(StringComparer.Ordinal);
            foreach (var presenter in Presenters)
            {
                index[presenter.Descriptor.TypeId] = presenter;
            }

            return index;
        }

        private static Dictionary<string, Dictionary<string, CounterOptionDescriptor>> BuildOptionIndex()
        {
            var index = new Dictionary<string, Dictionary<string, CounterOptionDescriptor>>(StringComparer.Ordinal);
            foreach (var presenter in Presenters)
            {
                var options = new Dictionary<string, CounterOptionDescriptor>(StringComparer.Ordinal);
                foreach (var option in presenter.Descriptor.Options)
                {
                    options[option.Key] = option;
                }

                index[presenter.Descriptor.TypeId] = options;
            }

            return index;
        }
    }
}
