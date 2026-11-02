using System;
using System.Linq;
using NotCounters.Util;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRUIControls;
using Zenject;

namespace NotCounters.Editor
{
    internal static class EditorUi
    {
        public const int UiLayer = Hud.HudMath.UiLayer;

        public const float UnitsPerMetre = 1000f;

        private const string FontName = Hud.HudAssetNames.Font;

        private const string NoGlowMaterialName = Hud.HudAssetNames.NoGlowMaterial;

        private static TMP_FontAsset? _font;
        private static Material? _noGlowMaterial;
        private static bool _materialLookupDone;

        public static TMP_FontAsset? Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Safe.Invoke("EditorUi.Font", FindFont, null);
                }

                return _font;
            }
        }

        public static Material? NoGlowMaterial
        {
            get
            {
                if (_noGlowMaterial == null && !_materialLookupDone)
                {
                    _materialLookupDone = true;
                    _noGlowMaterial = Safe.Invoke(
                        "EditorUi.NoGlowMaterial",
                        () => Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m != null && m.name == NoGlowMaterialName),
                        null);
                    if (_noGlowMaterial == null)
                    {
                        Safe.WarnOnce("EditorUi.NoGlowMaterial", "UINoGlow material not found; editor panels use the default UI material.");
                    }
                }

                return _noGlowMaterial;
            }
        }

        public static RectTransform CreateRect(string name, Transform? parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            return rect;
        }

        public static Canvas AddInteractiveCanvas(GameObject go, int sortingOrder, DiContainer container)
        {
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.TexCoord2
                | AdditionalCanvasShaderChannels.Normal
                | AdditionalCanvasShaderChannels.Tangent;

            var group = go.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            var raycaster = container.InstantiateComponent<VRGraphicRaycaster>(go);

            // default mask lets any collider within 6m block the canvas
            raycaster._blockingMask = 0;
            return canvas;
        }

        public static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            var material = NoGlowMaterial;
            if (material != null)
            {
                image.material = material;
            }

            image.color = color;
            image.raycastTarget = raycastTarget;

            image.canvasRenderer.cullTransparentMesh = false;
            return image;
        }

        public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize, TextAlignmentOptions alignment, Color color)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var font = Font;
            if (font != null)
            {
                label.font = font;
            }

            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.richText = false;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        public static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, 0f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        private static TMP_FontAsset? FindFont()
        {
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();

            var font = fonts.FirstOrDefault(f => f != null && f.name == FontName);
            if (font == null)
            {
                Safe.WarnOnce("EditorUi.Font", $"Font '{FontName}' not found; using TextMeshPro's default font.");
                font = TMP_Settings.defaultFontAsset != null
                    ? TMP_Settings.defaultFontAsset
                    : fonts.FirstOrDefault(f => f != null && f.name.IndexOf("Numbers", StringComparison.OrdinalIgnoreCase) < 0);
            }

            return font;
        }
    }
}
