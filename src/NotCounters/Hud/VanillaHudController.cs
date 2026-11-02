using System;
using System.Collections.Generic;
using BGLib.Polyglot;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using NotCounters.Util;
using SiraUtil.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace NotCounters.Hud
{
    internal sealed class VanillaHudController : IInitializable, ILateTickable, IDisposable
    {
        private const int ReapplyFrames = 2;

        private readonly SiraLog _log;
        private readonly HudLayoutSource _layouts;
        private readonly HudAnchorProvider _anchors;
        private readonly HudStyleFactory _styles;
        private readonly VanillaTextFormatter _formatter;
        private readonly IEditableElementRegistry _registry;
        private readonly AdvancedHudActivator _advancedHud;
        private readonly Dictionary<string, VanillaElementAdapter> _adapters = new Dictionary<string, VanillaElementAdapter>(StringComparer.Ordinal);
        private readonly List<VanillaBackdrop> _backdrops = new List<VanillaBackdrop>();

        private CoreGameHUDController? _hud;
        private GameEnergyUIPanel? _energyPanel;
        private int _reapplyFramesLeft;
        private int _adoptedBatterySegments;
        private bool _applied;

        private bool? _loggedUniformText;

        public VanillaHudController(
            SiraLog log,
            HudLayoutSource layouts,
            HudAnchorProvider anchors,
            HudStyleFactory styles,
            VanillaTextFormatter formatter,
            IEditableElementRegistry registry,
            AdvancedHudActivator advancedHud)
        {
            _log = log;
            _layouts = layouts;
            _anchors = anchors;
            _styles = styles;
            _formatter = formatter;
            _registry = registry;
            _advancedHud = advancedHud;
        }

        public void Initialize()
        {
            Safe.Invoke("hud:vanilla-init", () =>
            {
                _hud = _anchors.HudController;
                var frame = _anchors.VanillaFrame;
                if (_hud == null || frame == null)
                {
                    _log.Debug("No vanilla HUD to edit in this scene.");
                    return;
                }

                _styles.Prime();
                var hud = _hud;
                CreateAdapters(_hud.transform, frame);
                Safe.Invoke("hud:vanilla-advanced-capture", () => _advancedHud.Capture(hud, _adapters.Values));

                _layouts.Changed += HandleLayoutChanged;
                ApplyAll();
                _reapplyFramesLeft = ReapplyFrames;
            });
        }

        public void LateTick()
        {
            if (_adapters.Count == 0)
            {
                return;
            }

            try
            {
                if (_reapplyFramesLeft > 0)
                {
                    _reapplyFramesLeft--;
                    ApplyAll();
                }

                AdoptBatterySegments();
                if (_adapters.TryGetValue(VanillaElementIds.Energy, out var energy))
                {
                    energy.EnforceHiddenEffects();
                }
            }
            catch (Exception ex)
            {
                Safe.Report("hud:vanilla-late", ex);
            }
        }

        public void Dispose()
        {
            _layouts.Changed -= HandleLayoutChanged;
            foreach (var adapter in _adapters.Values)
            {
                _registry.Unregister(adapter);

                Safe.Invoke("hud:vanilla-restore-text:" + adapter.Id, adapter.RestoreTextLooks);
            }

            _adapters.Clear();
            _backdrops.Clear();
            _formatter.Track(null, null, null);
        }

        private void HandleLayoutChanged() => ApplyAll();

        private void ApplyAll()
        {
            if (!_layouts.IsEnabled)
            {
                if (_applied)
                {
                    RestoreVanilla();
                }

                return;
            }

            var layout = Safe.Invoke("hud:vanilla-layout", _layouts.GetLayout, null!);
            if (layout == null)
            {
                return;
            }

            var uniformText = _styles.UniformText;
            if (_loggedUniformText != uniformText)
            {
                _loggedUniformText = uniformText;
                _log.Debug($"Same text style everywhere: {(uniformText ? "on (vanilla texts upright, COMBO caption full width)" : "off (vanilla text styles)")}");
            }

            foreach (var adapter in _adapters.Values)
            {
                var settings = layout.Vanilla.TryGetValue(adapter.Id, out var configured) && configured != null
                    ? configured
                    : DefaultSettings();
                Safe.Invoke("hud:vanilla-apply:" + adapter.Id, () =>
                {
                    adapter.HiddenInPlay = _advancedHud.IsHiddenInPlay(adapter.Id);
                    adapter.UniformText = uniformText;
                    adapter.Apply(settings, VanillaElementCatalog.Get(adapter.Id));
                });
            }

            foreach (var backdrop in _backdrops)
            {
                var settings = layout.Vanilla.TryGetValue(backdrop.Id, out var configured) && configured != null
                    ? configured
                    : DefaultSettings();
                Safe.Invoke("hud:vanilla-apply:" + backdrop.Id, () => backdrop.Apply(settings, VanillaElementCatalog.Get(backdrop.Id)));
            }

            if (Safe.Invoke("hud:vanilla-advanced", () => _advancedHud.Apply(layout), false))
            {
                _reapplyFramesLeft = ReapplyFrames;
            }

            SyncRegistry(layout);
            Safe.Invoke("hud:vanilla-formats", () => ConfigureFormatter(layout));
            _applied = true;
        }

        private void SyncRegistry(HudLayout layout)
        {
            foreach (var adapter in _adapters.Values)
            {
                Safe.Invoke("hud:vanilla-register:" + adapter.Id, () =>
                {
                    if (VanillaElementPresence.IsRemoved(layout, adapter.Id))
                    {
                        _registry.Unregister(adapter);
                    }
                    else
                    {
                        _registry.Register(adapter);
                    }
                });
            }
        }

        private void RestoreVanilla()
        {
            foreach (var adapter in _adapters.Values)
            {
                Safe.Invoke("hud:vanilla-restore:" + adapter.Id, () =>
                {
                    adapter.HiddenInPlay = false;
                    adapter.UniformText = false;
                    adapter.Apply(DefaultSettings(), null);
                });
            }

            foreach (var backdrop in _backdrops)
            {
                Safe.Invoke("hud:vanilla-restore:" + backdrop.Id, () => backdrop.Apply(DefaultSettings(), null));
            }

            Safe.Invoke("hud:vanilla-advanced-restore", () => _advancedHud.Restore());
            foreach (var adapter in _adapters.Values)
            {
                _registry.Register(adapter);
            }

            Safe.Invoke("hud:vanilla-formats", () => _formatter.Configure(NumberGrouping.None, null, TimeDisplayMode.Default));
            _applied = false;
        }

        private void ConfigureFormatter(HudLayout layout)
        {
            var grouping = Setting(layout, VanillaElementIds.Score, d => d.SupportsNumberGrouping)?.NumberGrouping ?? NumberGrouping.None;
            var decimals = Setting(layout, VanillaElementIds.RelativeScore, d => d.SupportsDecimals)?.Decimals;
            var timeMode = Setting(layout, VanillaElementIds.SongProgress, d => d.SupportsTimeMode)?.TimeMode ?? TimeDisplayMode.Default;
            _formatter.Configure(grouping, decimals is int count ? Mathf.Clamp(count, 0, 6) : null, timeMode);
        }

        private VanillaElementSettings? Setting(HudLayout layout, string id, Func<VanillaElementDescriptor, bool> supports)
        {
            if (!_adapters.ContainsKey(id) || !layout.Vanilla.TryGetValue(id, out var settings) || settings == null)
            {
                return null;
            }

            var descriptor = VanillaElementCatalog.Get(id);
            return descriptor == null || supports(descriptor) ? settings : null;
        }

        private static VanillaElementSettings DefaultSettings() => new VanillaElementSettings();

        private void CreateAdapters(Transform hudRoot, Transform frame)
        {
            var combo = hudRoot.GetComponentInChildren<ComboUIController>(true);
            var score = hudRoot.GetComponentInChildren<ScoreUIController>(true);
            var rankPanel = hudRoot.GetComponentInChildren<ImmediateRankUIPanel>(true);
            var multiplier = hudRoot.GetComponentInChildren<ScoreMultiplierUIController>(true);
            var progress = hudRoot.GetComponentInChildren<SongProgressUIController>(true);
            _energyPanel = hudRoot.GetComponentInChildren<GameEnergyUIPanel>(true);

            TryCreate(VanillaElementIds.Combo, combo != null ? combo.transform : null, frame, adapter => SetUpCombo(adapter, combo!));
            TryCreate(VanillaElementIds.Score, score != null && score._scoreText != null ? score._scoreText.transform : null, frame,
                adapter => adapter.AddText(score!._scoreText, VanillaColorRole.Primary));
            TryCreate(VanillaElementIds.Rank, rankPanel != null && rankPanel._rankText != null ? rankPanel._rankText.transform : null, frame,
                adapter => adapter.AddText(rankPanel!._rankText, VanillaColorRole.Primary));
            TryCreate(VanillaElementIds.RelativeScore, rankPanel != null && rankPanel._relativeScoreText != null ? rankPanel._relativeScoreText.transform : null, frame,
                adapter => adapter.AddText(rankPanel!._relativeScoreText, VanillaColorRole.Primary));
            TryCreate(VanillaElementIds.Multiplier, multiplier != null ? multiplier.transform : null, frame, adapter => SetUpMultiplier(adapter, multiplier!));
            TryCreate(VanillaElementIds.SongProgress, progress != null ? progress.transform : null, frame, adapter => SetUpSongProgress(adapter, progress!));
            TryCreate(VanillaElementIds.Energy, _energyPanel != null ? _energyPanel.transform : null, frame, adapter => SetUpEnergy(adapter, _energyPanel!));

            var leftPanel = combo != null ? combo.transform.parent : rankPanel != null ? rankPanel.transform.parent : null;
            var rightPanel = multiplier != null ? multiplier.transform.parent : progress != null ? progress.transform.parent : null;
            if (leftPanel != rightPanel)
            {
                TryCaptureBackdrop(VanillaElementIds.LeftPanelBackdrop, leftPanel, hudRoot, frame);
                TryCaptureBackdrop(VanillaElementIds.RightPanelBackdrop, rightPanel, hudRoot, frame);
            }

            _formatter.Track(score, rankPanel, progress);
            _log.Debug($"Vanilla HUD elements found: {string.Join(", ", _adapters.Keys)}; backdrops: {_backdrops.Count}");
        }

        private void TryCaptureBackdrop(string id, Transform? panel, Transform hudRoot, Transform frame)
        {
            if (panel == null || panel == hudRoot || panel == frame)
            {
                return;
            }

            Safe.Invoke("hud:vanilla-capture:" + id, () =>
            {
                var backdrop = VanillaBackdrop.TryCapture(id, panel, _layouts.IsEditSession);
                if (backdrop != null)
                {
                    _backdrops.Add(backdrop);
                }
            });
        }

        private void TryCreate(string id, Transform? root, Transform frame, Action<VanillaElementAdapter> setUp)
        {
            if (root == null)
            {
                Safe.WarnOnce("hud:vanilla-missing:" + id, $"[NotCounters] Vanilla HUD element '{id}' not found; it is left unchanged.");
                return;
            }

            Safe.Invoke("hud:vanilla-capture:" + id, () =>
            {
                var adapter = new VanillaElementAdapter(id, DisplayNameOf(id), root, frame, _styles, _layouts.IsEditSession);
                setUp(adapter);
                _adapters[id] = adapter;
            });
        }

        private static string DisplayNameOf(string id) =>
            VanillaElementCatalog.Get(id)?.DisplayName is { Length: > 0 } name ? name : id;

        private static void SetUpCombo(VanillaElementAdapter adapter, ComboUIController combo)
        {
            var root = combo.transform;
            adapter.AddText(combo._comboText, VanillaColorRole.Primary);

            var caption = HudTypography.FindComboCaption(combo);
            if (caption != null)
            {
                adapter.AddText(caption, VanillaColorRole.Primary);
                adapter.SetLabel(caption, caption.GetComponent<LocalizedTextMeshProUGUI>());
            }

            var lines = new List<Graphic>();
            for (var i = 0; i < root.childCount; i++)
            {
                var image = root.GetChild(i).GetComponent<Image>();
                if (image != null)
                {
                    lines.Add(image);
                    adapter.AddGraphic(image, VanillaColorRole.Secondary);
                }
            }

            adapter.SetFullComboLines(lines);
        }

        private static void SetUpMultiplier(VanillaElementAdapter adapter, ScoreMultiplierUIController multiplier)
        {
            foreach (var text in multiplier.GetComponentsInChildren<TMP_Text>(true))
            {
                adapter.AddText(text, VanillaColorRole.Primary);
            }

            foreach (var image in multiplier.GetComponentsInChildren<Image>(true))
            {
                adapter.AddGraphic(image, VanillaColorRole.Secondary);
            }
        }

        private static void SetUpSongProgress(VanillaElementAdapter adapter, SongProgressUIController progress)
        {
            foreach (var text in progress.GetComponentsInChildren<TMP_Text>(true))
            {
                adapter.AddText(text, VanillaColorRole.Secondary);
            }

            var textContainer = progress._progressMinutesText != null ? progress._progressMinutesText.transform.parent?.parent : null;
            foreach (var image in progress.GetComponentsInChildren<Image>(true))
            {
                var isTextGlyph = textContainer != null && image.transform.IsChildOf(textContainer);
                adapter.AddGraphic(image, isTextGlyph ? VanillaColorRole.Secondary : VanillaColorRole.Primary);
                if (isTextGlyph && image is HMUI.ImageView glyph && SitsAmongTimeDigits(glyph.transform))
                {
                    adapter.AddSlantedGlyph(glyph);
                }
            }

            var totalTime = new List<GameObject>();
            if (progress._durationRectTransform != null)
            {
                totalTime.Add(progress._durationRectTransform.gameObject);
            }

            if (textContainer != null)
            {
                for (var i = 0; i < textContainer.childCount; i++)
                {
                    var child = textContainer.GetChild(i);
                    if (child.GetComponent<Graphic>() != null && child != progress._durationRectTransform)
                    {
                        totalTime.Add(child.gameObject);
                    }
                }
            }

            adapter.SetTotalTimeObjects(totalTime);
        }

        private static bool SitsAmongTimeDigits(Transform image)
        {
            var group = image.parent;
            if (group == null)
            {
                return false;
            }

            for (var i = 0; i < group.childCount; i++)
            {
                if (group.GetChild(i).GetComponent<TMP_Text>() != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetUpEnergy(VanillaElementAdapter adapter, GameEnergyUIPanel energy)
        {
            adapter.AddGraphic(energy._energyBar, VanillaColorRole.Primary);

            var background = energy.transform.Find("BarBG");
            if (background != null)
            {
                adapter.AddGraphic(background.GetComponent<Graphic>(), VanillaColorRole.Secondary);
            }

            foreach (var light in energy.GetComponentsInChildren<BloomPrePassLight>(true))
            {
                foreach (var renderer in light.GetComponentsInChildren<Renderer>(true))
                {
                    adapter.AddEffect(renderer);
                }

                foreach (var box in light.GetComponentsInChildren<ParametricBoxController>(true))
                {
                    adapter.AddEffect(box);
                }

                foreach (var sprite in light.GetComponentsInChildren<Parametric3SliceSpriteController>(true))
                {
                    adapter.AddEffect(sprite);
                }

                adapter.AddEffect(light);
            }
        }

        private void AdoptBatterySegments()
        {
            var segments = _energyPanel != null ? _energyPanel._batteryLifeSegments : null;
            if (!_applied || segments == null || segments.Count == _adoptedBatterySegments
                || !_adapters.TryGetValue(VanillaElementIds.Energy, out var adapter))
            {
                return;
            }

            _adoptedBatterySegments = segments.Count;
            foreach (var segment in segments)
            {
                adapter.AddGraphic(segment, VanillaColorRole.Primary);
            }

            adapter.Reapply();
        }
    }
}
