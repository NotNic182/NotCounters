using System;
using System.Collections.Generic;
using BGLib.Polyglot;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NotCounters.Hud
{
    internal enum VanillaColorRole
    {
        Primary,
        Secondary,
    }

    internal sealed class VanillaElementAdapter : IEditableElement
    {
        private readonly Transform _frame;
        private readonly HudStyleFactory _styles;
        private readonly bool _isEditSession;
        private readonly Vector3 _authoredPosition;
        private readonly Quaternion _authoredRotation;
        private readonly Vector3 _authoredScale;

        private readonly float _offsetScale;
        private readonly List<TextPart> _texts = new List<TextPart>();
        private readonly List<GraphicPart> _graphics = new List<GraphicPart>();
        private readonly List<GameObject> _fullComboLines = new List<GameObject>();
        private readonly List<GameObject> _totalTimeObjects = new List<GameObject>();
        private readonly List<EffectPart> _effects = new List<EffectPart>();
        private readonly List<GlyphPart> _glyphs = new List<GlyphPart>();

        private VanillaElementSettings _settings = new VanillaElementSettings();
        private bool _effectsHidden;
        private VanillaElementDescriptor? _descriptor;

        private TMP_Text? _label;
        private LocalizedTextMeshProUGUI? _labelLocalizer;
        private string? _labelKey;
        private string _labelOriginalText = string.Empty;
        private bool _labelOverridden;

        public VanillaElementAdapter(string id, string displayName, Transform root, Transform frame, HudStyleFactory styles, bool isEditSession)
        {
            Id = id;
            DisplayName = displayName;
            Root = root;
            _frame = frame;
            _styles = styles;
            _isEditSession = isEditSession;

            _authoredPosition = frame.InverseTransformPoint(root.position);
            _authoredRotation = Quaternion.Inverse(frame.rotation) * root.rotation;
            _authoredScale = root.localScale;
            _offsetScale = OffsetScale(id, root, frame);
        }

        private static float OffsetScale(string id, Transform root, Transform frame)
        {
            var reference = HudFollow.ReferenceElementScale(id);
            if (reference <= 0f)
            {
                return 1f;
            }

            var scale = Mathf.Abs(root.lossyScale.x) / (Mathf.Max(Mathf.Abs(frame.lossyScale.x), 1e-6f) * reference);
            return scale >= 0.05f && scale <= 20f ? scale : 1f;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public EditableElementKind Kind => EditableElementKind.Vanilla;

        public Transform Root { get; }

        public bool IsVisible => _settings.Visible;

        public bool HiddenInPlay { get; set; }

        public bool UniformText { get; set; }

        public void AddText(TMP_Text? text, VanillaColorRole role)
        {
            if (text == null || _texts.Exists(p => p.Text == text))
            {
                return;
            }

            _texts.Add(new TextPart(text, role, isElementRoot: text.transform == Root));
        }

        public void AddGraphic(Graphic? graphic, VanillaColorRole role)
        {
            if (graphic == null || graphic is TMP_Text || _graphics.Exists(p => p.Graphic == graphic))
            {
                return;
            }

            _graphics.Add(new GraphicPart(graphic, role));
        }

        public void AddSlantedGlyph(HMUI.ImageView? image)
        {
            if (image == null || !HudTextUniformity.IsSlantedGlyph(image._skew) || _glyphs.Exists(p => p.Image == image))
            {
                return;
            }

            _glyphs.Add(new GlyphPart(image));
        }

        public void RestoreTextLooks()
        {
            UniformText = false;
            foreach (var part in _texts)
            {
                if (part.Text != null)
                {
                    ApplyTextLook(part, uniform: false);
                }
            }

            foreach (var glyph in _glyphs)
            {
                if (glyph.Image != null)
                {
                    ApplyGlyphLook(glyph, uniform: false);
                }
            }
        }

        public void SetLabel(TMP_Text? label, LocalizedTextMeshProUGUI? localizer)
        {
            _label = label;
            _labelLocalizer = localizer;
            _labelOriginalText = label != null ? label.text ?? string.Empty : string.Empty;
        }

        public void SetFullComboLines(IEnumerable<Graphic> lines)
        {
            foreach (var line in lines)
            {
                _fullComboLines.Add(line.gameObject);
            }
        }

        public void SetTotalTimeObjects(IEnumerable<GameObject> objects)
        {
            _totalTimeObjects.AddRange(objects);
        }

        public void AddEffect(Behaviour? behaviour)
        {
            if (behaviour != null && !_effects.Exists(p => p.Behaviour == behaviour))
            {
                _effects.Add(new EffectPart(behaviour, null));
            }
        }

        public void AddEffect(Renderer? renderer)
        {
            if (renderer != null && !_effects.Exists(p => p.Renderer == renderer))
            {
                _effects.Add(new EffectPart(null, renderer));
            }
        }

        public void EnforceHiddenEffects()
        {
            if (!_effectsHidden)
            {
                return;
            }

            foreach (var part in _effects)
            {
                if (part.Enabled)
                {
                    part.Enabled = false;
                }
            }
        }

        public void Apply(VanillaElementSettings settings, VanillaElementDescriptor? descriptor)
        {
            _settings = settings;
            _descriptor = descriptor;
            Reapply();
        }

        public void Reapply()
        {
            ApplyPose(_settings);
            ApplyVisibility();
            ApplyStyle();
        }

        public Vector2 GetWorldSize()
        {
            var lossy = Root.lossyScale;
            return Root is RectTransform rect
                ? new Vector2(rect.rect.width * Mathf.Abs(lossy.x), rect.rect.height * Mathf.Abs(lossy.y))
                : new Vector2(0.5f, 0.5f);
        }

        public Vector2 GetLocalContentCentre() => Root is RectTransform rect ? rect.rect.center : Vector2.zero;

        public AnchorPose GetPose() =>
            new AnchorPose(
                _frame.InverseTransformPoint(Root.position),
                Quaternion.Inverse(_frame.rotation) * Root.rotation,
                HudMath.UniformScale(Root.localScale) / HudMath.UniformScale(_authoredScale));

        public void SetPose(AnchorPose pose)
        {
            Root.SetPositionAndRotation(_frame.TransformPoint(pose.Position), _frame.rotation * pose.Rotation);
            Root.localScale = _authoredScale * pose.Scale;
        }

        public void CommitToConfig(HudLayout editableLayout)
        {
            var pose = GetPose();
            var settings = GetOrCreateSettings(editableLayout);
            var inverse = Quaternion.Inverse(_authoredRotation);
            settings.PositionOffset = HudMath.ToConfigPosition(inverse * (pose.Position - _authoredPosition) / _offsetScale);
            settings.RotationOffset = HudMath.ToConfigEuler(inverse * pose.Rotation);
            settings.Scale = HudMath.ToConfigScale(pose.Scale);
            settings.Visible = _settings.Visible;
            _settings = settings;
        }

        public void ResetToDefault(HudLayout editableLayout)
        {
            var settings = GetOrCreateSettings(editableLayout);
            settings.PositionOffset = Vec3.Zero;
            settings.RotationOffset = Vec3.Zero;
            settings.Scale = 1f;
            _settings = settings;
            ApplyPose(settings);
        }

        public void SetVisibleInLayout(HudLayout editableLayout, bool visible)
        {
            var settings = GetOrCreateSettings(editableLayout);
            settings.Visible = visible;
            _settings = settings;
            ApplyVisibility();
        }

        private VanillaElementSettings GetOrCreateSettings(HudLayout layout)
        {
            if (!layout.Vanilla.TryGetValue(Id, out var settings) || settings == null)
            {
                settings = DefaultConfigFactory.CreateVanillaDefaults(Id);
                layout.Vanilla[Id] = settings;
            }

            return settings;
        }

        private void ApplyPose(VanillaElementSettings settings)
        {
            var position = _authoredPosition + (_authoredRotation * (settings.PositionOffset.ToUnity() * _offsetScale));
            var rotation = _authoredRotation * Quaternion.Euler(settings.RotationOffset.ToUnity());
            Root.SetPositionAndRotation(_frame.TransformPoint(position), _frame.rotation * rotation);
            Root.localScale = _authoredScale * settings.Scale;
        }

        private void ApplyVisibility()
        {
            // CanvasGroup instead of SetActive, SetActive resets the combo/multiplier animators
            var shown = _settings.Visible && !_settings.Removed && !(_isEditSession && HiddenInPlay);
            SetGroupAlpha(Root.gameObject, shown ? 1f : _isEditSession && !_settings.Removed ? HudMath.GhostAlpha : 0f);
            ApplyEffects(!_settings.Visible || _settings.Removed);
        }

        private void ApplyEffects(bool hide)
        {
            if (hide)
            {
                _effectsHidden = true;
                EnforceHiddenEffects();
                return;
            }

            if (!_effectsHidden)
            {
                return;
            }

            _effectsHidden = false;
            foreach (var part in _effects)
            {
                if (part.Renderer != null)
                {
                    part.Enabled = part.OriginalEnabled;
                }
            }

            foreach (var part in _effects)
            {
                if (part.Behaviour != null)
                {
                    part.Enabled = part.OriginalEnabled;
                }
            }
        }

        private void ApplyStyle()
        {
            var d = _descriptor;
            var s = _settings;
            ColorValue? primary = d == null || d.SupportsPrimaryColor ? s.PrimaryColor : null;
            ColorValue? secondary = d == null || d.SupportsSecondaryColor ? s.SecondaryColor : null;
            var sizeMultiplier = d == null || d.SupportsFontSize
                ? Mathf.Clamp(s.FontSizeMultiplier, ConfigValidator.MinFontSizeMultiplier, ConfigValidator.MaxFontSizeMultiplier)
                : 1f;

            var wrapping = sizeMultiplier > 1f ? false : (bool?)null;
            var outlineWidth = d == null || d.SupportsOutline ? s.OutlineWidth : 0f;

            foreach (var part in _texts)
            {
                if (part.Text == null)
                {
                    continue;
                }

                var color = part.Role == VanillaColorRole.Primary ? primary : secondary;
                part.Text.color = color is ColorValue c ? HudMath.Tint(c, part.OriginalColor.a) : part.OriginalColor;
                part.Text.fontSize = part.OriginalFontSize * sizeMultiplier;
                var wrap = wrapping ?? part.OriginalWordWrapping;
                if (part.Text.enableWordWrapping != wrap)
                {
                    part.Text.enableWordWrapping = wrap;
                }

                if (part.OriginalMaterial != null)
                {
                    var material = _styles.GetOutlineMaterial(part.OriginalMaterial, outlineWidth, s.OutlineColor);
                    if (part.Text.fontSharedMaterial != material)
                    {
                        part.Text.fontSharedMaterial = material;
                    }
                }

                ApplyTextLook(part, UniformText);
            }

            foreach (var glyph in _glyphs)
            {
                if (glyph.Image != null)
                {
                    ApplyGlyphLook(glyph, UniformText);
                }
            }

            foreach (var part in _graphics)
            {
                if (part.Graphic == null)
                {
                    continue;
                }

                var color = part.Role == VanillaColorRole.Primary ? primary : secondary;
                part.Graphic.color = color is ColorValue c ? HudMath.Tint(c, part.OriginalColor.a) : part.OriginalColor;
            }

            ApplyLabel(d == null || d.SupportsLabelText ? s.LabelText : null);

            var hideLines = (d == null || d.SupportsHideFullComboLines) && s.HideFullComboLines;
            foreach (var line in _fullComboLines)
            {
                SetGroupAlpha(line, hideLines ? 0f : 1f);
            }

            var timeMode = d == null || d.SupportsTimeMode ? s.TimeMode : TimeDisplayMode.Default;
            var hideTotal = timeMode == TimeDisplayMode.Elapsed || timeMode == TimeDisplayMode.Remaining;
            foreach (var go in _totalTimeObjects)
            {
                SetGroupAlpha(go, hideTotal ? 0f : 1f);
            }
        }

        private void ApplyLabel(string? labelText)
        {
            if (_label == null)
            {
                return;
            }

            if (labelText != null)
            {
                if (_labelLocalizer != null)
                {
                    _labelKey = _labelLocalizer.Key;
                    _labelLocalizer.enabled = false;
                    Object.Destroy(_labelLocalizer);
                    _labelLocalizer = null;
                }

                if (_label.text != labelText)
                {
                    _label.text = labelText;
                }

                _labelOverridden = true;
                return;
            }

            if (_labelOverridden)
            {
                _label.text = LocalizedOriginalLabel();
                _labelOverridden = false;
            }
        }

        private string LocalizedOriginalLabel()
        {
            if (_labelKey is not { Length: > 0 } key)
            {
                return _labelOriginalText;
            }

            try
            {
                var localized = Localization.Get(key);
                return string.IsNullOrEmpty(localized) ? _labelOriginalText : localized;
            }
            catch (Exception)
            {
                return _labelOriginalText;
            }
        }

        private static void ApplyTextLook(TextPart part, bool uniform)
        {
            var text = part.Text;
            if (part.AuthoredItalic)
            {
                var style = (int)text.fontStyle;
                if (uniform)
                {
                    if (HudTextUniformity.IsItalic(style))
                    {
                        text.fontStyle = (FontStyles)HudTextUniformity.WithItalic(style, italic: false);
                        part.ItalicRemoved = true;
                    }
                }
                else if (part.ItalicRemoved)
                {
                    text.fontStyle = (FontStyles)HudTextUniformity.WithItalic(style, italic: true);
                    part.ItalicRemoved = false;
                }
            }

            if (part.Widens)
            {
                var rect = text.rectTransform;
                var scale = rect.localScale;
                if (uniform)
                {
                    if (scale.x != part.UniformScaleX)
                    {
                        scale.x = part.UniformScaleX;
                        rect.localScale = scale;
                        part.Widened = true;
                    }
                }
                else if (part.Widened)
                {
                    scale.x = part.OriginalScaleX;
                    rect.localScale = scale;
                    part.Widened = false;
                }
            }
        }

        private static void ApplyGlyphLook(GlyphPart part, bool uniform)
        {
            var image = part.Image;
            if (uniform)
            {
                var upright = HudTextUniformity.GlyphSkew(part.OriginalSkew, uniform: true);
                if (image._skew != upright)
                {
                    image._skew = upright;
                    image.SetVerticesDirty();
                    part.Straightened = true;
                }
            }
            else if (part.Straightened)
            {
                image._skew = part.OriginalSkew;
                image.SetVerticesDirty();
                part.Straightened = false;
            }
        }

        private static void SetGroupAlpha(GameObject? go, float alpha)
        {
            if (go == null)
            {
                return;
            }

            var group = go.GetComponent<CanvasGroup>();
            if (group == null)
            {
                if (alpha >= 1f)
                {
                    return;
                }

                group = go.AddComponent<CanvasGroup>();
            }

            group.alpha = alpha;
        }

        private sealed class TextPart
        {
            public TextPart(TMP_Text text, VanillaColorRole role, bool isElementRoot)
            {
                Text = text;
                Role = role;
                OriginalFontSize = text.fontSize;
                OriginalColor = text.color;
                OriginalMaterial = text.fontSharedMaterial;
                OriginalWordWrapping = text.enableWordWrapping;
                AuthoredItalic = HudTextUniformity.IsItalic((int)text.fontStyle);
                var scale = text.rectTransform.localScale;
                OriginalScaleX = scale.x;
                UniformScaleX = HudTextUniformity.VanillaScaleX(scale.x, scale.y, uniform: true);
                Widens = HudTextUniformity.WidensVanillaText(scale.x, scale.y, isElementRoot);
            }

            public TMP_Text Text { get; }

            public VanillaColorRole Role { get; }

            public float OriginalFontSize { get; }

            public bool OriginalWordWrapping { get; }

            public Color OriginalColor { get; }

            public Material? OriginalMaterial { get; }

            public bool AuthoredItalic { get; }

            public bool Widens { get; }

            public float OriginalScaleX { get; }

            public float UniformScaleX { get; }

            public bool ItalicRemoved { get; set; }

            public bool Widened { get; set; }
        }

        private sealed class GlyphPart
        {
            public GlyphPart(HMUI.ImageView image)
            {
                Image = image;
                OriginalSkew = image._skew;
            }

            public HMUI.ImageView Image { get; }

            public float OriginalSkew { get; }

            public bool Straightened { get; set; }
        }

        private sealed class GraphicPart
        {
            public GraphicPart(Graphic graphic, VanillaColorRole role)
            {
                Graphic = graphic;
                Role = role;
                OriginalColor = graphic.color;
            }

            public Graphic Graphic { get; }

            public VanillaColorRole Role { get; }

            public Color OriginalColor { get; }
        }

        private sealed class EffectPart
        {
            public EffectPart(Behaviour? behaviour, Renderer? renderer)
            {
                Behaviour = behaviour;
                Renderer = renderer;
                OriginalEnabled = Enabled;
            }

            public Behaviour? Behaviour { get; }

            public Renderer? Renderer { get; }

            public bool OriginalEnabled { get; }

            public bool Enabled
            {
                get => Behaviour != null ? Behaviour.enabled : Renderer != null && Renderer.enabled;
                set
                {
                    if (Behaviour != null)
                    {
                        Behaviour.enabled = value;
                    }
                    else if (Renderer != null)
                    {
                        Renderer.enabled = value;
                    }
                }
            }
        }
    }
}
