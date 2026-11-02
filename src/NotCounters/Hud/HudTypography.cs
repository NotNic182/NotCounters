using System.Globalization;
using BGLib.Polyglot;
using NotCounters.Core.Counters;
using TMPro;
using UnityEngine;

namespace NotCounters.Hud
{
    internal readonly struct HudTextLook
    {
        public HudTextLook(FontStyles fontStyle, float characterSpacing, float wordSpacing, float lineSpacing, float widthScale)
        {
            FontStyle = fontStyle;
            CharacterSpacing = characterSpacing;
            WordSpacing = wordSpacing;
            LineSpacing = lineSpacing;
            WidthScale = widthScale;
        }

        public FontStyles FontStyle { get; }

        public float CharacterSpacing { get; }

        public float WordSpacing { get; }

        public float LineSpacing { get; }

        public float WidthScale { get; }

        public void ApplyTo(TMP_Text text, FontStyles extraStyles)
        {
            var fontStyle = FontStyle | extraStyles;
            if (text.fontStyle != fontStyle)
            {
                text.fontStyle = fontStyle;
            }

            text.characterSpacing = CharacterSpacing;
            text.wordSpacing = WordSpacing;
            text.lineSpacing = LineSpacing;
            var scale = new Vector3(WidthScale, 1f, 1f);
            if (text.rectTransform.localScale != scale)
            {
                text.rectTransform.localScale = scale;
            }
        }

        public HudTextLook For(bool uniform) => new HudTextLook(
            (FontStyles)HudTextUniformity.FontStyle((int)FontStyle, uniform),
            CharacterSpacing,
            WordSpacing,
            LineSpacing,
            HudTextUniformity.CounterWidthScale(WidthScale, uniform));

        public static HudTextLook Of(TMP_Text text)
        {
            var scale = text.rectTransform.localScale;
            var ratio = Mathf.Abs(scale.y) > 1e-6f ? Mathf.Abs(scale.x / scale.y) : 1f;

            if (!HudTextUniformity.IsTypographicWidthRatio(ratio))
            {
                ratio = 1f;
            }

            return new HudTextLook(text.fontStyle, text.characterSpacing, text.wordSpacing, text.lineSpacing, ratio);
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}, spacing {1:0.##}/{2:0.##}/{3:0.##}, width x{4:0.###}", FontStyle, CharacterSpacing, WordSpacing, LineSpacing, WidthScale);
    }

    internal sealed class HudTypography
    {
        internal const float CapCentreAboveLineCentrePerEm = (28.1f - 21.49f) / 89f;

        internal const float CapHeightPerEm = 56.2f / 89f;

        private const float ReferenceStackSpacing = 29f / 35f;

        private HudTypography(HudTextLook caption, HudTextLook value, float stackSpacing, string description, bool fromLiveHud)
        {
            Caption = caption;
            Value = value;
            StackSpacing = stackSpacing;
            Description = description;
            FromLiveHud = fromLiveHud;
        }

        public static HudTypography Reference { get; } = new HudTypography(
            new HudTextLook(FontStyles.UpperCase, 0f, 0f, 0f, 0.7f),
            new HudTextLook(FontStyles.Normal, 0f, 0f, 0f, 1f),
            ReferenceStackSpacing,
            "1.44.1 reference values",
            fromLiveHud: false);

        public HudTextLook Caption { get; }

        public HudTextLook Value { get; }

        public float StackSpacing { get; }

        public string Description { get; }

        public bool FromLiveHud { get; }

        public static HudTypography? Capture(CoreGameHUDController hud, Transform? frame)
        {
            var combo = hud.GetComponentInChildren<ComboUIController>(true);
            TMP_Text? number = combo != null ? combo._comboText : null;
            var caption = combo != null ? FindComboCaption(combo) : null;
            TMP_Text? value = number;
            if (value == null)
            {
                var rankPanel = hud.GetComponentInChildren<ImmediateRankUIPanel>(true);
                value = rankPanel != null ? rankPanel._relativeScoreText : null;
            }

            if (caption == null && value == null)
            {
                return null;
            }

            var captionLook = caption != null ? HudTextLook.Of(caption) : Reference.Caption;
            var valueLook = value != null ? HudTextLook.Of(value) : Reference.Value;
            var spacing = ReferenceStackSpacing;
            var sizes = "sizes not measured";
            if (combo != null && caption != null && number != null)
            {
                var panel = combo.transform;
                var captionEm = PanelEm(caption, panel);
                var numberEm = PanelEm(number, panel);
                var distance = LineCentreY(caption, panel) - LineCentreY(number, panel);
                var measured = distance / ((captionEm + numberEm) * 0.5f);

                if (!float.IsNaN(measured) && measured > 0.4f && measured < 1.6f)
                {
                    spacing = measured;
                }

                var metresPerUnit = Mathf.Abs(panel.lossyScale.y) / (frame != null ? Mathf.Max(1e-6f, Mathf.Abs(frame.lossyScale.y)) : 1f);
                sizes = string.Format(
                    CultureInfo.InvariantCulture,
                    "caption {0:0.###} m, number {1:0.###} m (ratio {2:0.###}), line centres {3:0.###} apart = {4:0.###} x mean size",
                    captionEm * metresPerUnit,
                    numberEm * metresPerUnit,
                    numberEm > 0f ? captionEm / numberEm : 0f,
                    distance,
                    measured);
            }

            var description = string.Format(
                CultureInfo.InvariantCulture,
                "caption [{0}] from {1}, value [{2}] from {3}; {4}; stack spacing {5:0.###}",
                captionLook,
                caption != null ? "\"" + caption.name + "\"" : "reference",
                valueLook,
                value != null ? "\"" + value.name + "\"" : "reference",
                sizes,
                spacing);
            return new HudTypography(captionLook, valueLook, spacing, description, fromLiveHud: true);
        }

        internal static TMP_Text? FindComboCaption(ComboUIController combo)
        {
            TMP_Text? firstOther = null;
            foreach (var text in combo.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || text == combo._comboText)
                {
                    continue;
                }

                if (text.GetComponent<LocalizedTextMeshProUGUI>() != null)
                {
                    return text;
                }

                firstOther ??= text;
            }

            return firstOther;
        }

        private static float PanelEm(TMP_Text text, Transform panel)
        {
            var panelScale = Mathf.Abs(panel.lossyScale.y);
            return panelScale > 1e-9f ? text.fontSize * Mathf.Abs(text.rectTransform.lossyScale.y) / panelScale : text.fontSize;
        }

        private static float LineCentreY(TMP_Text text, Transform panel)
        {
            var rect = text.rectTransform;
            return panel.InverseTransformPoint(rect.TransformPoint(rect.rect.center)).y;
        }
    }
}
