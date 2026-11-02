using System;
using System.Diagnostics;
using BeatSaberMarkupLanguage.FloatingScreen;
using HMUI;
using NotCounters.Contracts;
using NotCounters.Editor;
using NotCounters.Util;
using SiraUtil.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;
using Object = UnityEngine.Object;

namespace NotCounters.UI.Panels
{
    internal sealed class EditorPanels : IInitializable, ILateTickable, IDisposable
    {
        private static readonly Vector2 PanelSize = new Vector2(100f, 85f);

        private const float UnitsToMetres = 0.0135f;

        private static readonly Vector3 LeftPanelOffset = new Vector3(-1.5f, 1.25f, 0.9f);

        private const float PanelPitchDegrees = 10f;

        private readonly SiraLog _log;
        private readonly DiContainer _container;
        private readonly EditSessionController _session;
        private readonly IHudAnchorProvider? _anchorProvider;
        private readonly PanelSlot[] _slots;

        private bool _buildAttempted;
        private bool _visible;
        private bool _disposed;

        public EditorPanels(
            SiraLog log,
            DiContainer container,
            EditSessionController session,
            ElementsPanel elements,
            SessionPanel sessionPanel,
            [InjectOptional] IHudAnchorProvider? anchorProvider)
        {
            _log = log;
            _container = container;
            _session = session;
            _anchorProvider = anchorProvider;
            _slots = new[]
            {
                new PanelSlot(elements, LeftPanelOffset),
                new PanelSlot(sessionPanel, new Vector3(-LeftPanelOffset.x, LeftPanelOffset.y, LeftPanelOffset.z)),
            };
        }

        public void Initialize()
        {
            _session.EditingStateChanged += HandleEditingStateChanged;
            Safe.Invoke("EditorPanels.Build", EnsureBuilt);
        }

        public void LateTick()
        {
            if (!_visible || _disposed)
            {
                return;
            }

            FollowAnchor();
            foreach (var slot in _slots)
            {
                if (slot.Screen != null)
                {
                    Safe.Invoke(slot.LateTickKey, slot.LateTickStep);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Safe.Invoke("EditorPanels.Dispose", () =>
            {
                _session.EditingStateChanged -= HandleEditingStateChanged;
                HideHints();
                foreach (var slot in _slots)
                {
                    var panel = slot.Panel;
                    Safe.Invoke("EditorPanels.Detach:" + panel.Name, panel.Detach);
                    if (slot.Screen != null)
                    {
                        Object.Destroy(slot.Screen.gameObject);
                    }

                    slot.Screen = null;
                    slot.Hints = null;
                }
            });
        }

        private void HandleEditingStateChanged() => Safe.Invoke("EditorPanels.EditingStateChanged", () =>
        {
            if (_disposed)
            {
                return;
            }

            if (!_session.IsEditing)
            {
                SetVisible(false);
                return;
            }

            EnsureBuilt();
            SetVisible(true);
            FollowAnchor();
            foreach (var slot in _slots)
            {
                if (slot.Screen != null)
                {
                    var panel = slot.Panel;
                    Safe.Invoke("EditorPanels.Shown:" + panel.Name, panel.OnShown);
                }
            }
        });

        private void EnsureBuilt()
        {
            if (_buildAttempted || _disposed)
            {
                return;
            }

            _buildAttempted = true;
            var stopwatch = Stopwatch.StartNew();
            BuildAll();
            SetVisible(_session.IsEditing);
            _log.Debug($"The side panels were built in {stopwatch.ElapsedMilliseconds} ms{(_session.IsEditing ? string.Empty : " (hidden until editing starts)")}.");
        }

        private void BuildAll()
        {
            var built = 0;
            foreach (var slot in _slots)
            {
                if (TryBuild(slot))
                {
                    built++;
                }
            }

            if (built < _slots.Length)
            {
                _log.Warn(built == 0
                    ? "The NotCounters side panels could not be created. Elements can still be dragged; press the menu button for the pause menu, where \"Exit NotCounters\" leaves and keeps your edits."
                    : "A NotCounters side panel could not be created; the other one works (see the error above).");
            }
        }

        private bool TryBuild(PanelSlot slot)
        {
            var panel = slot.Panel;
            FloatingScreen? screen = null;
            try
            {
                var parser = PanelSupport.TryGetParser(panel.Name);
                if (parser == null)
                {
                    return false;
                }

                var view = PanelSupport.ReadView(panel.ViewResource);
                var radius = HorizontalDistance(slot.Offset) / UnitsToMetres;
                screen = PanelSupport.CreateScreen(panel.Name, PanelSize, UnitsToMetres, radius);
                slot.Screen = screen;
                Place(slot);

                panel.PrepareView();

                parser.Parse(view, screen.gameObject, panel);

                var container = _container;
                var hints = PanelSupport.CreateHintController(screen);
                slot.Hints = hints;
                PanelSupport.PrepareForGameplay(screen.gameObject, container, hints);
                panel.Attach(parser, root => PanelSupport.PrepareForGameplay(root, container, hints));
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"Could not create the NotCounters panel '{panel.Name}': {ex}");
                Safe.Invoke("EditorPanels.Cleanup:" + panel.Name, panel.Detach);
                if (screen != null)
                {
                    Object.Destroy(screen.gameObject);
                }

                slot.Screen = null;
                slot.Hints = null;
                return false;
            }
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible)
            {
                HideHints();
            }

            foreach (var slot in _slots)
            {
                var screen = slot.Screen;
                if (screen == null || screen.gameObject.activeSelf == visible)
                {
                    continue;
                }

                var root = screen.gameObject;
                if (!visible)
                {
                    Safe.Invoke("EditorPanels.CloseModals:" + slot.Panel.Name, () => PanelSupport.CloseModals(root));
                }

                root.SetActive(visible);
                if (visible)
                {
                    if (!root.activeInHierarchy)
                    {
                        Safe.Invoke("EditorPanels.Reroot:" + slot.Panel.Name, () => Reroot(root));
                    }

                    var hints = slot.Hints;
                    if (hints != null)
                    {
                        Safe.Invoke("EditorPanels.SecureHints:" + slot.Panel.Name, () => PanelSupport.SecureHints(hints));
                    }

                    Safe.Invoke("EditorPanels.FinishDismissals:" + slot.Panel.Name, () => PanelSupport.FinishDismissals(root));
                    Safe.Invoke("EditorPanels.RestoreDimming:" + slot.Panel.Name, () => PanelSupport.RestoreDimming(root));
                }
            }
        }

        private void HideHints()
        {
            foreach (var slot in _slots)
            {
                var hints = slot.Hints;
                if (hints != null && hints._hoverHintPanel != null)
                {
                    Safe.Invoke("EditorPanels.HideHints:" + slot.Panel.Name, () => hints.HideHintInstant(null));
                }
            }
        }

        private void Reroot(GameObject root)
        {
            var from = root.scene.name;
            root.transform.SetParent(null, worldPositionStays: true);
            SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());
            _log.Warn($"The NotCounters panel '{root.name}' was under an inactive object in scene '{from}'; moved to '{root.scene.name}'.");
        }

        private void FollowAnchor()
        {
            foreach (var slot in _slots)
            {
                if (slot.Screen != null)
                {
                    Place(slot);
                }
            }
        }

        private void Place(PanelSlot slot)
        {
            var offset = slot.Offset;
            var yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            var local = Quaternion.Euler(PanelPitchDegrees, yaw, 0f);
            var anchor = EditorAnchor.TryGet(_anchorProvider);
            if (anchor != null)
            {
                slot.Screen!.transform.SetPositionAndRotation(anchor.TransformPoint(offset), anchor.rotation * local);
            }
            else
            {
                slot.Screen!.transform.SetPositionAndRotation(offset, local);
            }
        }

        private static float HorizontalDistance(Vector3 offset) => Mathf.Max(0.5f, new Vector2(offset.x, offset.z).magnitude);

        private sealed class PanelSlot
        {
            public PanelSlot(IEditorPanel panel, Vector3 offset)
            {
                Panel = panel;
                Offset = offset;
                LateTickKey = "EditorPanels.LateTick:" + panel.Name;
                LateTickStep = panel.LateTick;
            }

            public IEditorPanel Panel { get; }

            public Vector3 Offset { get; }

            public FloatingScreen? Screen { get; set; }

            public HoverHintController? Hints { get; set; }

            public string LateTickKey { get; }

            public Action LateTickStep { get; }
        }
    }
}
