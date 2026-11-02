using System;
using System.Collections.Generic;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using NotCounters.Core.Stats;
using NotCounters.Util;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace NotCounters.Hud
{
    internal sealed class CounterManager : IInitializable, ITickable, IDisposable
    {
        private const int FaultThreshold = 3;

        private const float PeriodicRenderInterval = 1f;

        private readonly SiraLog _log;
        private readonly HudLayoutSource _layouts;
        private readonly IHudAnchorProvider _anchors;
        private readonly IStatsSource? _stats;
        private readonly HudStyleFactory _styles;
        private readonly IEditableElementRegistry _registry;
        private readonly StatsSnapshot _emptyStats = new StatsSnapshot();
        private readonly List<CounterSlot> _slots = new List<CounterSlot>();

        private bool _renderRequested;
        private float _nextPeriodicRender;
        private bool _initialized;

        public CounterManager(
            SiraLog log,
            HudLayoutSource layouts,
            IHudAnchorProvider anchors,
            HudStyleFactory styles,
            IEditableElementRegistry registry,
            [InjectOptional] IStatsSource? stats)
        {
            _log = log;
            _layouts = layouts;
            _anchors = anchors;
            _styles = styles;
            _registry = registry;
            _stats = stats;
        }

        private StatsSnapshot Stats => _stats?.Current ?? _emptyStats;

        public void Initialize()
        {
            Safe.Invoke("hud:counters-init", () =>
            {
                if (_stats == null)
                {
                    _log.Warn("No IStatsSource bound; counters render without data.");
                }
                else
                {
                    _stats.Updated += HandleStatsUpdated;
                }

                _layouts.Changed += HandleLayoutChanged;
                _initialized = true;
                Synchronize();
            });
        }

        public void Tick()
        {
            if (_slots.Count == 0)
            {
                return;
            }

            try
            {
                TickRender();
            }
            catch (Exception ex)
            {
                Safe.Report("hud:counters-tick", ex);
            }
        }

        private void TickRender()
        {
            var now = Time.unscaledTime;
            if (_renderRequested || now >= _nextPeriodicRender)
            {
                _renderRequested = false;
                _nextPeriodicRender = now + PeriodicRenderInterval;
                RenderAll();
            }

            foreach (var slot in _slots)
            {
                if (slot.View == null)
                {
                    continue;
                }

                try
                {
                    slot.View.Flush();
                }
                catch (Exception ex)
                {
                    Safe.Report(slot.FlushKey, ex);
                }
            }
        }

        public void Dispose()
        {
            if (_stats != null)
            {
                _stats.Updated -= HandleStatsUpdated;
            }

            _layouts.Changed -= HandleLayoutChanged;
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                RemoveSlot(i);
            }
        }

        private void HandleStatsUpdated() => _renderRequested = true;

        private void HandleLayoutChanged()
        {
            if (_initialized)
            {
                Synchronize();
            }
        }

        private void Synchronize()
        {
            var wanted = new List<CounterSettings>();
            if (_layouts.IsEnabled)
            {
                var layout = Safe.Invoke("hud:counters-layout", _layouts.GetLayout, null!);
                if (layout != null)
                {
                    foreach (var settings in layout.Counters)
                    {
                        if (settings != null && (settings.Enabled || _layouts.IsEditSession))
                        {
                            wanted.Add(settings);
                        }
                    }
                }
            }

            var wantedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var settings in wanted)
            {
                wantedIds.Add(settings.Id);
            }

            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                if (!wantedIds.Contains(_slots[i].Settings.Id))
                {
                    RemoveSlot(i);
                }
            }

            foreach (var settings in wanted)
            {
                var index = _slots.FindIndex(s => string.Equals(s.Settings.Id, settings.Id, StringComparison.Ordinal));
                if (index >= 0 && string.Equals(_slots[index].Settings.Type, settings.Type, StringComparison.Ordinal))
                {
                    UpdateSlot(_slots[index], settings);
                    continue;
                }

                if (index >= 0)
                {
                    RemoveSlot(index);
                }

                Safe.Invoke("hud:counter-create:" + settings.Type, () => CreateSlot(settings));
            }

            _renderRequested = false;
            _nextPeriodicRender = Time.unscaledTime + PeriodicRenderInterval;
            RenderAll();
        }

        private void CreateSlot(CounterSettings settings)
        {
            var presenter = CounterCatalog.Get(settings.Type);
            var descriptor = presenter?.Descriptor ?? CounterCatalog.GetDescriptor(settings.Type);
            if (presenter == null || descriptor == null)
            {
                Safe.WarnOnce("hud:unknown-counter:" + settings.Type, $"[NotCounters] Unknown counter type '{settings.Type}' skipped.");
                return;
            }

            var view = CounterView.Create(_anchors, settings, descriptor, _styles, _layouts.IsEditSession);
            _slots.Add(new CounterSlot(settings, presenter, view));
            _registry.Register(view);
        }

        private void UpdateSlot(CounterSlot slot, CounterSettings settings)
        {
            slot.Settings = settings;
            Safe.Invoke("hud:counter-style:" + settings.Type, () => slot.View.ApplySettings(settings, slot.View.Descriptor));

            if (_layouts.IsEditSession && slot.Faulted)
            {
                slot.Faulted = false;
                slot.ConsecutiveFailures = 0;
                Safe.Invoke("hud:counter-unfault:" + settings.Type, slot.View.ClearFault);
            }
        }

        private void RemoveSlot(int index)
        {
            var slot = _slots[index];
            _slots.RemoveAt(index);
            _registry.Unregister(slot.View);
            if (slot.View != null)
            {
                Object.Destroy(slot.View.gameObject);
            }
        }

        private void RenderAll()
        {
            var stats = Stats;
            var showErrors = _layouts.Global.ShowErrorsInCounters;
            foreach (var slot in _slots)
            {
                if (!slot.Faulted)
                {
                    Render(slot, stats, showErrors);
                }
            }
        }

        private void Render(CounterSlot slot, StatsSnapshot stats, bool showErrors)
        {
            try
            {
                var render = slot.Presenter.Render(stats, slot.Settings);
                var composed = CounterTextComposer.Compose(render, slot.Settings, slot.View.Descriptor);
                slot.View.ShowContent(composed);
                slot.ConsecutiveFailures = 0;
            }
            catch (Exception ex)
            {
                Safe.Report("hud:counter-render:" + slot.Settings.Type, ex);
                if (++slot.ConsecutiveFailures < FaultThreshold)
                {
                    return;
                }

                slot.Faulted = true;
                _log.Warn($"Counter '{slot.Settings.Type}' ({slot.Settings.Id}) failed {FaultThreshold} times in a row and is disabled for this level.");
                Safe.Invoke("hud:counter-fault:" + slot.Settings.Type, () => slot.View.ShowFault(showErrors));
            }
        }

        private sealed class CounterSlot
        {
            public CounterSlot(CounterSettings settings, ICounterPresenter presenter, CounterView view)
            {
                Settings = settings;
                Presenter = presenter;
                View = view;
                FlushKey = "hud:counter-flush:" + settings.Type;
            }

            public string FlushKey { get; }

            public CounterSettings Settings { get; set; }

            public ICounterPresenter Presenter { get; }

            public CounterView View { get; }

            public int ConsecutiveFailures { get; set; }

            public bool Faulted { get; set; }
        }
    }
}
