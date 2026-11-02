using System;
using System.Collections.Generic;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using NotCounters.Util;
using UnityEngine;
using Zenject;

namespace NotCounters.Hud
{
    internal sealed class AdvancedHudActivator
    {
        private readonly HudLayoutSource _layouts;
        private readonly bool _playerAdvancedHud;
        private readonly Dictionary<string, Element> _elements = new Dictionary<string, Element>(StringComparer.Ordinal);

        private CoreGameHUDController? _hud;

        public AdvancedHudActivator(HudLayoutSource layouts, [InjectOptional] GameplayCoreSceneSetupData? sceneSetupData)
        {
            _layouts = layouts;

            _playerAdvancedHud = sceneSetupData?.playerSpecificSettings?.advancedHud ?? true;
        }

        public void Capture(CoreGameHUDController hud, IEnumerable<VanillaElementAdapter> elements)
        {
            _hud = hud;
            _elements.Clear();

            var gameObjects = new Dictionary<string, GameObject?>(StringComparer.Ordinal)
            {
                [VanillaElementIds.SongProgress] = hud.songProgressPanelGO,
                [VanillaElementIds.Rank] = hud.immediateRankGo,
                [VanillaElementIds.RelativeScore] = hud.relativeScoreGo,
                [VanillaElementIds.Energy] = hud.energyPanelGo,
            };

            foreach (var element in elements)
            {
                var root = new Switch(element.Root.gameObject);
                var shown = root;
                if (gameObjects.TryGetValue(element.Id, out var gameObject) && gameObject != null && gameObject != root.Target)
                {
                    Safe.WarnOnce(
                        "hud:vanilla-active-root:" + element.Id,
                        $"[NotCounters] The game switches '{gameObject.name}' for the vanilla {element.DisplayName}, not its root '{root.Target.name}'; both are tracked.");
                    shown = new Switch(gameObject);
                }

                _elements[element.Id] = new Element(root, shown);
            }
        }

        public bool IsHiddenInPlay(string elementId) =>
            _layouts.IsEditSession
            && AdvancedHudElements.IsHiddenInPlay(elementId, _playerAdvancedHud, _layouts.Global.ForceAdvancedHudElements);

        public bool Apply(HudLayout layout)
        {
            if (_hud == null)
            {
                return false;
            }

            var hudShown = _hud.gameObject.activeSelf;
            var force = _layouts.Global.ForceAdvancedHudElements;
            var isEditSession = _layouts.IsEditSession;
            var activated = false;
            foreach (var pair in _elements)
            {
                var id = pair.Key;
                var element = pair.Value;
                activated |= Safe.Invoke("hud:vanilla-active:" + id, () =>
                {
                    var settings = layout.Vanilla.TryGetValue(id, out var configured) ? configured : null;
                    var removed = settings != null && settings.Removed;
                    var visible = settings == null || settings.Visible;
                    return element.Apply(VanillaElementPresence.Target(id, removed, visible, force, isEditSession, hudShown));
                }, false);
            }

            return activated;
        }

        public bool Restore()
        {
            var activated = false;
            foreach (var pair in _elements)
            {
                var element = pair.Value;
                activated |= Safe.Invoke("hud:vanilla-active-restore:" + pair.Key, element.Restore, false);
            }

            return activated;
        }

        private sealed class Element
        {
            private readonly Switch _root;
            private readonly Switch _shown;

            public Element(Switch root, Switch shown)
            {
                _root = root;
                _shown = shown;
            }

            public bool Apply(ElementActiveTarget target)
            {
                switch (target)
                {
                    case ElementActiveTarget.Inactive:
                        if (_shown != _root)
                        {
                            _shown.Restore();
                        }

                        _root.Set(false);
                        return false;

                    case ElementActiveTarget.Active:
                    {
                        var activated = _shown != _root && _root.Restore();
                        return _shown.Set(true) | activated;
                    }

                    default:
                        return Restore();
                }
            }

            public bool Restore()
            {
                var activated = _root.Restore();
                if (_shown != _root)
                {
                    activated |= _shown.Restore();
                }

                return activated;
            }
        }

        private sealed class Switch
        {
            private readonly bool _vanillaActive;
            private bool _changed;

            public Switch(GameObject target)
            {
                Target = target;
                _vanillaActive = target.activeSelf;
            }

            public GameObject Target { get; }

            public bool Set(bool active)
            {
                if (Target == null || Target.activeSelf == active)
                {
                    return false;
                }

                Target.SetActive(active);
                _changed = true;
                return active;
            }

            public bool Restore()
            {
                if (!_changed || Target == null)
                {
                    return false;
                }

                _changed = false;
                if (Target.activeSelf == _vanillaActive)
                {
                    return false;
                }

                Target.SetActive(_vanillaActive);
                return _vanillaActive;
            }
        }
    }
}
