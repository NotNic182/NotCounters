using System;
using System.Collections.Generic;
using System.Globalization;
using NotCounters.Core.Config;
using NotCounters.Util;
using SiraUtil.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NotCounters.Hud
{
    internal sealed class HudStyleFactory : IDisposable
    {
        private const string HudFontName = HudAssetNames.Font;

        private const string CurvedKeyword = "CURVED";

        private readonly SiraLog _log;
        private readonly HudAnchorProvider _anchors;
        private readonly HudLayoutSource _layouts;
        private readonly Dictionary<string, Material> _outlineMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

        private readonly Dictionary<int, Material> _cloneBases = new Dictionary<int, Material>();

        private bool _textResolved;
        private TMP_FontAsset? _font;
        private Material? _textMaterial;
        private HudTypography _typography = HudTypography.Reference;
        private bool _imageMaterialResolved;
        private Material? _imageMaterial;

        public HudStyleFactory(SiraLog log, HudAnchorProvider anchors, HudLayoutSource layouts, LeaderboardLogos logos)
        {
            _log = log;
            _anchors = anchors;
            _layouts = layouts;
            Logos = logos;
        }

        public bool UniformText => Safe.Invoke("hud:style-uniform", ReadUniformText, false);

        public HudTypography Typography
        {
            get
            {
                ResolveText();
                return _typography;
            }
        }

        public Material? ImageMaterial
        {
            get
            {
                if (!_imageMaterialResolved)
                {
                    _imageMaterialResolved = true;
                    _imageMaterial = Safe.Invoke<Material?>("hud:style-image", ResolveImageMaterial, null);
                }

                return _imageMaterial;
            }
        }

        public LeaderboardLogos Logos { get; }

        public TMP_FontAsset? Font
        {
            get
            {
                ResolveText();
                return _font;
            }
        }

        public Material? TextMaterial
        {
            get
            {
                ResolveText();
                return _textMaterial;
            }
        }

        public Material? GetCounterTextMaterial(float outlineWidth, ColorValue outlineColor)
        {
            var baseMaterial = TextMaterial;
            return baseMaterial == null ? null : GetOutlineMaterial(baseMaterial, outlineWidth, outlineColor);
        }

        public Material GetOutlineMaterial(Material baseMaterial, float outlineWidth, ColorValue outlineColor)
        {
            baseMaterial = Unwrap(baseMaterial);
            var width = Mathf.Clamp01(outlineWidth);
            if (width <= 0f)
            {
                return baseMaterial;
            }

            var key = baseMaterial.GetInstanceID().ToString(CultureInfo.InvariantCulture)
                + "|" + width.ToString("0.###", CultureInfo.InvariantCulture)
                + "|" + outlineColor.ToHex();
            if (_outlineMaterials.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            ShaderUtilities.GetShaderPropertyIDs();
            var material = new Material(baseMaterial) { name = baseMaterial.name + " (NotCounters outline " + key + ")" };
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            material.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor.ToUnity());

            _outlineMaterials[key] = material;
            _cloneBases[material.GetInstanceID()] = baseMaterial;
            return material;
        }

        public void Prime() => ResolveText();

        public void Dispose()
        {
            foreach (var material in _outlineMaterials.Values)
            {
                if (material != null)
                {
                    Object.Destroy(material);
                }
            }

            _outlineMaterials.Clear();
            _cloneBases.Clear();
        }

        private bool ReadUniformText() => _layouts.Global?.UniformHudText ?? false;

        private void ResolveText()
        {
            if (_textResolved)
            {
                return;
            }

            _textResolved = true;
            Safe.Invoke("hud:style-text", () =>
            {
                if (TryFromVanillaHud(out _font, out _textMaterial)
                    || TryByName(out _font, out _textMaterial))
                {
                    _log.Debug($"Counter font: {_font!.name}, material: {_textMaterial!.name}");
                }
                else
                {
                    _log.Warn("HUD font not found; counters fall back to the default TMP font.");
                }
            });

            Safe.Invoke("hud:style-typography", () =>
            {
                var hud = _anchors.HudController;
                var captured = hud != null ? HudTypography.Capture(hud, _anchors.VanillaFrame) : null;
                _typography = captured ?? HudTypography.Reference;
                _log.Debug($"Counter typography ({(captured != null ? "live HUD" : "no vanilla HUD, reference values")}): {_typography.Description}");
            });
        }

        private Material? ResolveImageMaterial()
        {
            var hud = _anchors.HudController;
            if (hud != null)
            {
                foreach (var image in hud.GetComponentsInChildren<Image>(true))
                {
                    var material = image != null ? image.material : null;
                    if (material != null && material.name == HudAssetNames.NoGlowMaterial)
                    {
                        return material;
                    }
                }
            }

            var byName = FindByName<Material>(HudAssetNames.NoGlowMaterial);
            if (byName != null)
            {
                return byName;
            }

            var bsml = BeatSaberMarkupLanguage.Utilities.ImageResources.NoGlowMat;
            if (bsml == null)
            {
                _log.Warn("UINoGlow material not found; the PP counter's logos use the default UI material.");
            }

            return bsml;
        }

        private bool TryFromVanillaHud(out TMP_FontAsset? font, out Material? material)
        {
            font = null;
            material = null;
            var hud = _anchors.HudController;
            var combo = hud != null ? hud.GetComponentInChildren<ComboUIController>(true) : null;

            var text = combo != null ? combo._comboText : null;
            if (text == null)
            {
                return false;
            }

            var candidate = text.fontSharedMaterial;
            if (candidate != null)
            {
                candidate = Unwrap(candidate);
                if (candidate.IsKeywordEnabled(ShaderUtilities.Keyword_Outline) || candidate.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay))
                {
                    candidate = text.font != null ? text.font.material : null;
                }
            }

            return Accept(text.font, candidate, out font, out material);
        }

        private Material Unwrap(Material material) =>
            _cloneBases.TryGetValue(material.GetInstanceID(), out var original) && original != null ? original : material;

        private static bool TryByName(out TMP_FontAsset? font, out Material? material)
        {
            var candidate = FindByName<TMP_FontAsset>(HudFontName);
            return Accept(candidate, candidate != null ? candidate.material : null, out font, out material);
        }

        private static bool Accept(TMP_FontAsset? candidateFont, Material? candidateMaterial, out TMP_FontAsset? font, out Material? material)
        {
            font = null;
            material = null;
            if (candidateFont == null || candidateMaterial == null || candidateMaterial.IsKeywordEnabled(CurvedKeyword))
            {
                return false;
            }

            font = candidateFont;
            material = candidateMaterial;
            return true;
        }

        private static T? FindByName<T>(string name)
            where T : Object
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<T>())
            {
                if (candidate != null && candidate.name == name)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
