using System;
using System.Collections.Generic;
using HMUI;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotCounters.Hud
{
    internal sealed class CounterView : MonoBehaviour, IEditableElement, IAttachableElement
    {
        internal const float MetresPerUnit = 0.02f;

        private const AdditionalCanvasShaderChannels VanillaShaderChannels = (AdditionalCanvasShaderChannels)27;

        private const int SortingOrder = -2;

        private const string FaultText = "ERR";

        private const string ReservedDetailText = "+0.00 %";

        private static readonly Color FaultColor = new Color(1f, 0.35f, 0.35f, 1f);

        private readonly List<RowSlot> _rows = new List<RowSlot>();
        private readonly List<CounterRowMetrics> _rowMetrics = new List<CounterRowMetrics>();
        private readonly List<CounterRowPlacement> _rowPlacements = new List<CounterRowPlacement>();

        private readonly List<CounterRowMetrics> _firstLineMetrics = new List<CounterRowMetrics>();
        private readonly List<CounterRowPlacement> _firstLinePlacements = new List<CounterRowPlacement>();

        private readonly MeasureCache<Vector2> _valueMeasure = new MeasureCache<Vector2>();
        private readonly MeasureCache<Vector2> _detailMeasure = new MeasureCache<Vector2>();
        private readonly MeasureCache<Vector2> _labelMeasure = new MeasureCache<Vector2>();

        private IHudAnchorProvider _anchors = null!;

        private Transform _anchor = null!;

        private float _appliedPull = 1f;

        private float _appliedClearance;

        private float _posedBlockBottom = -1f;

        private float _blockBottom;

        private float _posedBlockHeight = -1f;
        private float _posedBlockWidth = -1f;

        private FollowMemo? _followMemo;

        private HudStyleFactory _styles = null!;
        private bool _isEditSession;
        private CounterTypeDescriptor _descriptor = null!;
        private RectTransform _canvas = null!;
        private CanvasGroup _content = null!;
        private CurvedTextMeshPro _value = null!;
        private CurvedTextMeshPro _label = null!;
        private CurvedTextMeshPro _detail = null!;

        private bool _hasContent;
        private ComposedCounterText _composed;
        private bool _faulted;
        private bool _showErrors;
        private bool _dirty;
        private Vector2 _blockSize;

        private float _stackSpacing;

        private float _valueEm;
        private float _captionEm;
        private HudTextLook _valueLook;
        private HudTextLook _captionLook;
        private FontStyles _extraStyles;
        private HorizontalAlignmentOptions _horizontal;
        private Material? _textMaterial;

        private int _styleVersion;

        private Vector2 _contentCentreLocal;

        internal CounterSettings Settings { get; private set; } = null!;

        internal CounterTypeDescriptor Descriptor => _descriptor;

        public string Id => Settings.Id;

        public string DisplayName => string.IsNullOrEmpty(Settings.Name) ? _descriptor.DisplayName : Settings.Name!;

        public EditableElementKind Kind => EditableElementKind.Counter;

        public Transform Root => transform;

        public bool IsVisible => Settings.Enabled;

        internal static CounterView Create(IHudAnchorProvider anchors, CounterSettings settings, CounterTypeDescriptor descriptor, HudStyleFactory styles, bool isEditSession)
        {
            var root = new GameObject("NotCountersCounter " + settings.Type) { layer = HudMath.UiLayer };
            try
            {
                root.SetActive(false);
                var anchor = anchors.Anchor;
                root.transform.SetParent(anchor, worldPositionStays: false);

                var view = root.AddComponent<CounterView>();
                view._anchors = anchors;
                view._anchor = anchor;
                view._styles = styles;
                view._isEditSession = isEditSession;
                view.BuildHierarchy();
                view.ApplySettings(settings, descriptor);

                root.SetActive(true);

                view.Flush();
                return view;
            }
            catch
            {
                root.SetActive(false);
                Destroy(root);
                throw;
            }
        }

        internal void ApplySettings(CounterSettings settings, CounterTypeDescriptor descriptor)
        {
            Settings = settings;
            _descriptor = descriptor;
            ApplyPose(settings);
            ApplyStyle(settings.Style);
            MarkDirty();
        }

        public void Reattach(CounterSettings settings, CounterAttachment attachment)
        {
            if (attachment != CounterAttachment.None && _followMemo is { } memo && memo.StaysAt(settings))
            {
                memo.Restore(settings, attachment);
                return;
            }

            var followed = settings.Attachment != CounterAttachment.None && attachment == CounterAttachment.None
                ? new FollowMemo(settings)
                : (FollowMemo?)null;
            WritePose(settings, attachment);
            if (followed is { } turnedOff)
            {
                _followMemo = turnedOff.WithStaysPutPose(settings);
            }
        }

        internal void ShowContent(in ComposedCounterText composed)
        {
            if (!_faulted && _hasContent && SameContent(_composed, composed))
            {
                return;
            }

            _composed = composed;
            _hasContent = true;
            _faulted = false;
            MarkDirty();
        }

        internal void ShowFault(bool showErrors)
        {
            if (_faulted && _showErrors == showErrors)
            {
                return;
            }

            _faulted = true;
            _showErrors = showErrors;
            MarkDirty();
        }

        internal void ClearFault()
        {
            if (_faulted)
            {
                _faulted = false;
                MarkDirty();
            }
        }

        internal void Flush()
        {
            if (_dirty && gameObject.activeInHierarchy)
            {
                Refresh();
            }
        }

        public Vector2 GetWorldSize()
        {
            var fontSize = Mathf.Max(0.5f, Settings.Style.FontSize);
            var width = Mathf.Max(_blockSize.x, fontSize * 2f);
            var height = Mathf.Max(_blockSize.y, fontSize);
            var scale = Mathf.Abs(transform.lossyScale.x) * MetresPerUnit;
            return new Vector2(width * scale, height * scale);
        }

        public Vector2 GetLocalContentCentre() => _contentCentreLocal;

        public AnchorPose GetPose() =>
            new AnchorPose(
                _anchor.InverseTransformPoint(transform.position),
                Quaternion.Inverse(_anchor.rotation) * transform.rotation,
                HudMath.UniformScale(transform.localScale));

        public void SetPose(AnchorPose pose)
        {
            transform.SetPositionAndRotation(_anchor.TransformPoint(pose.Position), _anchor.rotation * pose.Rotation);
            transform.localScale = Vector3.one * pose.Scale;
        }

        public void CommitToConfig(HudLayout editableLayout)
        {
            var target = Find(editableLayout);
            if (target == null)
            {
                return;
            }

            WritePose(target, target.Attachment);
            Settings = target;
        }

        public void ResetToDefault(HudLayout editableLayout)
        {
            var target = Find(editableLayout) ?? Settings;
            target.Rotation = Vec3.Zero;
            target.Scale = 1f;
            Settings = target;
            ApplyPose(target);
            MarkDirty();
        }

        private void WritePose(CounterSettings settings, CounterAttachment attachment)
        {
            var drawn = new AnchorPose(
                _anchor.InverseTransformPoint(transform.position),
                Quaternion.Inverse(_anchor.rotation) * transform.rotation,
                HudMath.UniformScale(transform.localScale));
            var configured = attachment != CounterAttachment.None
                ? Unfollow(settings, attachment, drawn)
                : _anchors.FromAnchor(attachment, drawn);
            settings.Attachment = attachment;
            settings.Position = HudMath.ToConfigPosition(configured.Position);
            settings.Rotation = HudMath.ToConfigEuler(configured.Rotation);
            settings.Scale = HudMath.ToConfigScale(configured.Scale);
        }

        private AnchorPose Unfollow(CounterSettings settings, CounterAttachment attachment, AnchorPose drawn)
        {
            var eye = _anchors.EyePosition;
            var pull = Mathf.Max(_appliedPull, HudFollow.MinPullFactor);
            var unpulled = new AnchorPose(eye + ((drawn.Position - eye) / pull), drawn.Rotation, drawn.Scale / pull);
            PoseBlock(settings.Style, out var blockHeight, out var blockBottom);

            AnchorPose Unmap(float clearance)
            {
                var mapped = unpulled;
                mapped.Position.y -= clearance;
                return _anchors.FromAnchor(attachment, mapped);
            }

            float ClearanceAt(float clearance)
            {
                var configured = Unmap(clearance);
                var mapped = _anchors.ToAnchor(attachment, configured.Position, configured.Rotation, configured.Scale);
                return EnergyBarClearance(configured.Position, configured.Scale, mapped, blockHeight, blockBottom);
            }

            return Unmap(HudFollow.SettleClearance(_appliedClearance, ClearanceAt));
        }

        private void PoseBlock(TextStyle style, out float height, out float bottom)
        {
            height = _blockSize.y > 0f ? _blockSize.y : Mathf.Max(0.5f, style.FontSize);
            bottom = _blockBottom > 0f ? _blockBottom : height * 0.5f;
        }

        public void SetVisibleInLayout(HudLayout editableLayout, bool visible)
        {
            var target = Find(editableLayout) ?? Settings;
            target.Enabled = visible;
            Settings = target;
            MarkDirty();
        }

        private CounterSettings? Find(HudLayout layout)
        {
            foreach (var counter in layout.Counters)
            {
                if (counter != null && string.Equals(counter.Id, Settings.Id, StringComparison.Ordinal))
                {
                    return counter;
                }
            }

            return null;
        }

        private void BuildHierarchy()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform)) { layer = HudMath.UiLayer };
            _canvas = (RectTransform)canvasGo.transform;
            _canvas.SetParent(transform, worldPositionStays: false);
            _canvas.localScale = Vector3.one * MetresPerUnit;
            _canvas.pivot = new Vector2(0.5f, 0.5f);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.additionalShaderChannels = VanillaShaderChannels;
            canvas.sortingOrder = SortingOrder;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 1f;
            scaler.referencePixelsPerUnit = 10f;

            _content = canvasGo.AddComponent<CanvasGroup>();
            _content.interactable = false;
            _content.blocksRaycasts = false;

            _label = CreateText("Label", _canvas);
            _value = CreateText("Value", _canvas);
            _detail = CreateText("Detail", _canvas);
            _detail.enabled = false;
        }

        private RowSlot GetRowSlot(int index)
        {
            while (_rows.Count <= index)
            {
                var number = _rows.Count;
                var slot = new RowSlot(
                    CreateLogo($"Row{number} Logo", _canvas),
                    CreateText($"Row{number} Badge", _canvas),
                    CreateText($"Row{number} Text", _canvas));
                StyleRow(slot);
                _rows.Add(slot);
            }

            return _rows[index];
        }

        private Image CreateLogo(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = HudMath.UiLayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

            // default UI material writes alpha and the bloom turns that into glow
            var image = go.AddComponent<Image>();
            var material = _styles.ImageMaterial;
            if (material != null)
            {
                image.material = material;
            }

            image.raycastTarget = false;
            image.preserveAspect = true;
            image.type = Image.Type.Simple;
            image.enabled = false;
            return image;
        }

        private CurvedTextMeshPro CreateText(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = HudMath.UiLayer };

            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

            var text = go.AddComponent<CurvedTextMeshPro>();
            var font = _styles.Font;
            if (font != null)
            {
                text.font = font;
            }

            text.text = string.Empty;
            go.SetActive(true);

            // after Awake, TMP turns word wrapping back on when it loads its defaults
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.richText = false;
            return text;
        }

        private void ApplyPose(CounterSettings settings)
        {
            var pose = _anchors.ToAnchor(settings.Attachment, settings.Position.ToUnity(), Quaternion.Euler(settings.Rotation.ToUnity()), settings.Scale);
            PoseBlock(settings.Style, out var blockHeight, out var blockBottom);
            var pull = 1f;
            var clearance = 0f;
            var eye = _anchors.EyePosition;
            if (settings.Attachment != CounterAttachment.None)
            {
                clearance = EnergyBarClearance(settings.Position.ToUnity(), settings.Scale, pose, blockHeight, blockBottom);
                pose.Position.y += clearance;
                var bottomMetres = blockBottom * MetresPerUnit * Mathf.Abs(pose.Scale);
                pull = HudFollow.PullFactor(eye.y, pose.Position.y, bottomMetres, _anchors.FloorHeight);
            }

            var position = eye + ((pose.Position - eye) * pull);
            transform.SetPositionAndRotation(_anchor.TransformPoint(position), _anchor.rotation * pose.Rotation);
            transform.localScale = Vector3.one * (pose.Scale * pull);
            _appliedPull = pull;
            _appliedClearance = clearance;
            _posedBlockBottom = blockBottom;
            _posedBlockHeight = blockHeight;
            _posedBlockWidth = _blockSize.x;
        }

        private float EnergyBarClearance(Vector3 referencePosition, float referenceScale, in AnchorPose mapped, float blockHeight, float blockBottom)
        {
            if (!(_anchors.EnergyBarSpan is { } bar))
            {
                return 0f;
            }

            var ownScale = Mathf.Abs(referenceScale);
            var mappedScale = Mathf.Abs(mapped.Scale);
            var centreX = referencePosition.x + (_contentCentreLocal.x * ownScale);
            var halfWidth = _blockSize.x * MetresPerUnit * ownScale * 0.5f;
            if (Mathf.Abs(centreX) - halfWidth >= HudFollow.ReferenceEnergyHalfWidth)
            {
                return 0f;
            }

            var above = (blockHeight - blockBottom) * MetresPerUnit;
            var below = blockBottom * MetresPerUnit;
            return HudFollow.EnergyBarClearance(
                referencePosition.y + (above * ownScale),
                referencePosition.y - (below * ownScale),
                mapped.Position.y + (above * mappedScale),
                mapped.Position.y - (below * mappedScale),
                bar.y,
                bar.x,
                ownScale > 1e-6f ? mappedScale / ownScale : 1f);
        }

        private void RefreshPoseForBlock()
        {
            if (Settings.Attachment != CounterAttachment.None
                && (_anchors.FloorHeight.HasValue || _anchors.EnergyBarSpan.HasValue)
                && (Mathf.Abs(_blockBottom - _posedBlockBottom) > 0.25f
                    || Mathf.Abs(_blockSize.y - _posedBlockHeight) > 0.25f
                    || Mathf.Abs(_blockSize.x - _posedBlockWidth) > 0.5f))
            {
                ApplyPose(Settings);
            }
        }

        private void ApplyStyle(TextStyle style)
        {
            _styleVersion++;

            var fontSize = Mathf.Clamp(style.FontSize, ConfigValidator.MinFontSize, ConfigValidator.MaxFontSize);
            var labelScale = Mathf.Clamp(style.LabelScale, ConfigValidator.MinLabelScale, ConfigValidator.MaxLabelScale);
            _extraStyles = (style.Bold ? FontStyles.Bold : FontStyles.Normal) | (style.Italic ? FontStyles.Italic : FontStyles.Normal);
            _horizontal = style.Alignment switch
            {
                TextAlign.Left => HorizontalAlignmentOptions.Left,
                TextAlign.Right => HorizontalAlignmentOptions.Right,
                _ => HorizontalAlignmentOptions.Center,
            };

            var typography = _styles.Typography;
            var uniform = _styles.UniformText;
            _stackSpacing = typography.StackSpacing;
            _valueEm = fontSize;
            _captionEm = fontSize * labelScale;
            _valueLook = typography.Value.For(uniform);
            _captionLook = typography.Caption.For(uniform);
            _textMaterial = _styles.GetCounterTextMaterial(style.OutlineWidth, style.OutlineColor);
            StyleText(_value, _valueEm, _valueLook, _extraStyles, _horizontal, _textMaterial);
            StyleText(_label, _captionEm, _captionLook, _extraStyles, _horizontal, _textMaterial);
            StyleText(_detail, _captionEm, _valueLook, _extraStyles, _horizontal, _textMaterial);
            foreach (var slot in _rows)
            {
                StyleRow(slot);
            }
        }

        private void StyleRow(RowSlot slot)
        {
            StyleText(slot.Text, _valueEm, _valueLook, _extraStyles, _horizontal, _textMaterial);
            StyleText(slot.Badge, _captionEm, _captionLook, _extraStyles, HorizontalAlignmentOptions.Center, _textMaterial);
        }

        private static void StyleText(TMP_Text text, float fontSize, in HudTextLook look, FontStyles extraStyles, HorizontalAlignmentOptions horizontal, Material? material)
        {
            text.fontSize = fontSize;
            look.ApplyTo(text, extraStyles);
            text.horizontalAlignment = horizontal;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            if (material != null && text.fontSharedMaterial != material)
            {
                text.fontSharedMaterial = material;
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
            if (gameObject.activeInHierarchy)
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            _dirty = false;

            var enabledInLayout = Settings.Enabled;
            string value;
            string? label;
            string? detail = null;
            var detailColor = Color.white;
            var reserveDetail = false;
            IReadOnlyList<CounterRow>? rows;
            CounterTextLayoutKind layout;
            Color valueColor;
            Color labelColor;
            bool contentShown;

            if (_faulted)
            {
                value = FaultText;
                label = _hasContent ? _composed.Label : null;
                rows = null;
                layout = _hasContent ? _composed.Layout : CounterTextLayoutKind.ValueOnly;
                valueColor = FaultColor;
                labelColor = Settings.Style.LabelColor.ToUnity();
                contentShown = _showErrors;
            }
            else if (_hasContent)
            {
                value = _composed.Value ?? string.Empty;
                label = _composed.Label;
                detail = _composed.Detail;
                detailColor = _composed.DetailColor.ToUnity();
                reserveDetail = _composed.ReservesDetail;
                rows = _composed.Rows;
                layout = _composed.Layout;
                valueColor = _composed.ValueColor.ToUnity();
                labelColor = _composed.LabelColor.ToUnity();
                contentShown = !_composed.Hidden;
            }
            else
            {
                value = string.Empty;
                label = null;
                rows = null;
                layout = CounterTextLayoutKind.ValueOnly;
                valueColor = Settings.Style.ValueColor.ToUnity();
                labelColor = Settings.Style.LabelColor.ToUnity();
                contentShown = false;
            }

            if (!contentShown && _isEditSession && string.IsNullOrEmpty(value))
            {
                value = DisplayName;
                rows = null;
                detail = null;
                reserveDetail = false;
            }

            var alpha = contentShown && enabledInLayout ? 1f : _isEditSession ? HudMath.GhostAlpha : 0f;
            _content.alpha = alpha;
            if (alpha <= 0f)
            {
                foreach (var slot in _rows)
                {
                    slot.Logo.enabled = false;
                }

                return;
            }

            if (rows != null)
            {
                _detail.enabled = false;
                WriteRows(rows, label, layout, valueColor, labelColor);
            }
            else
            {
                WriteTexts(value, label, layout, valueColor, labelColor, detail, detailColor, reserveDetail);
            }

            RefreshPoseForBlock();
        }

        private void WriteTexts(string value, string? label, CounterTextLayoutKind layout, Color valueColor, Color labelColor, string? detail, Color detailColor, bool reserveDetail)
        {
            var hasCaption = IsCaptionLayout(layout) && !string.IsNullOrEmpty(label);
            HideRows(0);
            _value.enabled = true;
            SetText(_value, value);
            SetColor(_value, valueColor);
            WriteCaption(hasCaption ? label : null, labelColor);

            var valueLocal = PreferredSize(_value, _valueMeasure);
            var valueSize = ScaledSize(_value, valueLocal);
            var valueLines = LineCount(value);
            var hasDetail = !string.IsNullOrEmpty(detail);
            _detail.enabled = hasDetail;
            if (!hasDetail && !reserveDetail)
            {
                var content = CounterContentBox.ForText(valueSize.x, valueSize.y, valueLines);
                var block = PlaceCaption(content, hasCaption ? layout : CounterTextLayoutKind.ValueOnly);
                Place(_value.rectTransform, new Vector2(block.ContentX, block.ContentY), valueLocal);
                PinBlock(block);
                return;
            }

            Vector2 detailLocal;
            if (hasDetail)
            {
                SetText(_detail, detail!);
                SetColor(_detail, detailColor);
                detailLocal = PreferredSize(_detail, _detailMeasure);
            }
            else
            {
                detailLocal = _detail.GetPreferredValues(ReservedDetailText);
            }

            var detailSize = ScaledSize(_detail, detailLocal);
            var stack = CounterBlockLayout.StackDetail(
                valueSize.x,
                valueSize.y,
                valueLines,
                detailSize.x,
                detailSize.y,
                _valueEm,
                _captionEm,
                _stackSpacing,
                Settings.Style.Alignment);
            var arranged = PlaceCaption(stack.Box, hasCaption ? layout : CounterTextLayoutKind.ValueOnly);
            var contentCentre = new Vector2(arranged.ContentX, arranged.ContentY);
            Place(_value.rectTransform, contentCentre + new Vector2(stack.ValueX, stack.ValueY), valueLocal);
            if (hasDetail)
            {
                Place(_detail.rectTransform, contentCentre + new Vector2(stack.DetailX, stack.DetailY), detailLocal);
            }

            PinBlock(arranged, Mathf.Max(0f, stack.Box.Height - valueSize.y));
        }

        private void WriteRows(IReadOnlyList<CounterRow> rows, string? title, CounterTextLayoutKind layout, Color valueColor, Color labelColor)
        {
            var hasTitle = IsCaptionLayout(layout) && !string.IsNullOrEmpty(title);
            _value.enabled = false;
            WriteCaption(hasTitle ? title : null, labelColor);

            var logoTint = new Color(1f, 1f, 1f, labelColor.a);
            var markHeight = CounterBlockLayout.RowLogoMarkHeight(_captionEm);
            _rowMetrics.Clear();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var slot = GetRowSlot(i);
                slot.Text.enabled = true;
                SetText(slot.Text, row.Value);
                SetColor(slot.Text, valueColor);
                slot.TextSize = PreferredSize(slot.Text, slot.TextMeasure);
                var textSize = ScaledSize(slot.Text, slot.TextSize);

                var logoSize = Vector2.zero;
                var logoIsText = false;
                if (_styles.Logos.TryGet(row.IconKey, out var logo))
                {
                    slot.Badge.enabled = false;
                    ShowLogo(slot, logo, logoTint);
                    var aspect = logo.Sprite.rect.height > 0f ? logo.Sprite.rect.width / logo.Sprite.rect.height : 1f;
                    logoSize = new Vector2(markHeight * aspect, markHeight);

                    slot.LogoSize = logo.MarkHeight > 0f ? logoSize / logo.MarkHeight : logoSize;
                }
                else
                {
                    slot.Logo.enabled = false;
                    var badge = CounterIcons.BadgeText(row.IconKey);
                    slot.Badge.enabled = badge != null;
                    if (badge != null)
                    {
                        SetText(slot.Badge, badge);
                        SetColor(slot.Badge, labelColor);
                        slot.LogoSize = PreferredSize(slot.Badge, slot.BadgeMeasure);
                        logoSize = ScaledSize(slot.Badge, slot.LogoSize);
                        logoIsText = true;
                    }
                }

                _rowMetrics.Add(new CounterRowMetrics(textSize.x, textSize.y, LineCount(row.Value), logoSize.x, logoSize.y, logoIsText));
            }

            HideRows(rows.Count);
            var box = CounterBlockLayout.ArrangeRows(
                _rowMetrics,
                _valueEm,
                _captionEm,
                HudTypography.CapCentreAboveLineCentrePerEm,
                HudTypography.CapHeightPerEm,
                Settings.Style.Alignment,
                _rowPlacements);
            var extraBelow = ExtraRowLinesHeight(box.Height);
            var block = PlaceCaption(box, hasTitle ? layout : CounterTextLayoutKind.ValueOnly);
            var offset = new Vector2(block.ContentX, block.ContentY);
            for (var i = 0; i < rows.Count; i++)
            {
                var slot = _rows[i];
                var placement = _rowPlacements[i];
                Place(slot.Text.rectTransform, offset + new Vector2(placement.TextX, placement.TextY), slot.TextSize);
                var logoPosition = offset + new Vector2(placement.LogoX, placement.LogoY);
                Place(slot.Logo.rectTransform, logoPosition, slot.LogoSize);
                Place(slot.Badge.rectTransform, logoPosition, slot.LogoSize);
            }

            PinBlock(block, extraBelow);
        }

        private float ExtraRowLinesHeight(float height)
        {
            var multiLine = false;
            foreach (var row in _rowMetrics)
            {
                multiLine |= row.LineCount > 1;
            }

            if (!multiLine)
            {
                return 0f;
            }

            _firstLineMetrics.Clear();
            foreach (var row in _rowMetrics)
            {
                var lines = Math.Max(1, row.LineCount);
                _firstLineMetrics.Add(new CounterRowMetrics(row.TextWidth, row.TextHeight / lines, 1, row.LogoWidth, row.LogoHeight, row.LogoIsText));
            }

            var firstLines = CounterBlockLayout.ArrangeRows(
                _firstLineMetrics,
                _valueEm,
                _captionEm,
                HudTypography.CapCentreAboveLineCentrePerEm,
                HudTypography.CapHeightPerEm,
                Settings.Style.Alignment,
                _firstLinePlacements);
            return Mathf.Max(0f, height - firstLines.Height);
        }

        private void ShowLogo(RowSlot slot, in LeaderboardLogo logo, Color tint)
        {
            var image = slot.Logo;
            if (image.sprite != logo.Sprite)
            {
                image.sprite = logo.Sprite;
            }

            if (slot.LogoMaterial != logo.Material)
            {
                slot.LogoMaterial = logo.Material;
                image.material = logo.Material != null ? logo.Material : _styles.ImageMaterial;
            }

            SetColor(image, tint);
            image.enabled = true;
        }

        private void WriteCaption(string? caption, Color color)
        {
            if (caption != null)
            {
                SetText(_label, caption);
                SetColor(_label, color);
            }

            _label.enabled = caption != null;
        }

        private CounterBlockArrangement PlaceCaption(CounterContentBox content, CounterTextLayoutKind layout)
        {
            var captionLocal = layout != CounterTextLayoutKind.ValueOnly ? PreferredSize(_label, _labelMeasure) : Vector2.zero;
            var captionSize = ScaledSize(_label, captionLocal);
            var block = CounterBlockLayout.ArrangeCaption(
                content,
                captionSize.x,
                captionSize.y,
                LineCount(_label.text),
                layout,
                _valueEm,
                _captionEm,
                _stackSpacing,
                HudTypography.CapCentreAboveLineCentrePerEm,
                HudTypography.CapHeightPerEm,
                Settings.Style.Alignment);
            Place(_label.rectTransform, new Vector2(block.CaptionX, block.CaptionY), captionLocal);
            return block;
        }

        private void PinBlock(in CounterBlockArrangement arrangement, float extraBelow = 0f)
        {
            var pivotX = Settings.Style.Alignment switch
            {
                TextAlign.Left => 0f,
                TextAlign.Right => 1f,
                _ => 0.5f,
            };
            var block = new Vector2(arrangement.Width, arrangement.Height);
            var pivotY = block.y > 0f ? 0.5f + (Mathf.Clamp(extraBelow, 0f, block.y) * 0.5f / block.y) : 0.5f;
            _canvas.pivot = new Vector2(pivotX, pivotY);
            _canvas.sizeDelta = block;
            _blockSize = block;
            _blockBottom = pivotY * block.y;
            _contentCentreLocal = new Vector2((0.5f - pivotX) * block.x, (0.5f - pivotY) * block.y) * MetresPerUnit;
        }

        private void HideRows(int first)
        {
            for (var i = first; i < _rows.Count; i++)
            {
                var slot = _rows[i];
                slot.Logo.enabled = false;
                slot.Badge.enabled = false;
                slot.Text.enabled = false;
            }
        }

        private static bool IsCaptionLayout(CounterTextLayoutKind layout) =>
            layout == CounterTextLayoutKind.LabelAbove
            || layout == CounterTextLayoutKind.LabelBelow
            || layout == CounterTextLayoutKind.LabelLeft
            || layout == CounterTextLayoutKind.LabelRight;

        private Vector2 PreferredSize(TMP_Text text, MeasureCache<Vector2> measure)
        {
            var current = text.text;
            if (!measure.TryGet(current, _styleVersion, out var size))
            {
                size = text.GetPreferredValues();
                measure.Store(current, _styleVersion, size);
            }

            return size;
        }

        private static Vector2 ScaledSize(TMP_Text text, Vector2 preferred) =>
            new Vector2(preferred.x * Mathf.Abs(text.rectTransform.localScale.x), preferred.y);

        private static int LineCount(string? text)
        {
            var lines = 1;
            if (text != null)
            {
                foreach (var c in text)
                {
                    if (c == '\n')
                    {
                        lines++;
                    }
                }
            }

            return lines;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text.text != value)
            {
                text.text = value;
            }
        }

        private static void SetColor(Graphic graphic, Color color)
        {
            if (graphic.color != color)
            {
                graphic.color = color;
            }
        }

        private static bool SameContent(in ComposedCounterText a, in ComposedCounterText b) =>
            a.Hidden == b.Hidden
            && a.Layout == b.Layout
            && string.Equals(a.Value, b.Value, StringComparison.Ordinal)
            && string.Equals(a.Label, b.Label, StringComparison.Ordinal)
            && string.Equals(a.Detail, b.Detail, StringComparison.Ordinal)
            && a.ReservesDetail == b.ReservesDetail
            && a.ValueColor == b.ValueColor
            && a.LabelColor == b.LabelColor
            && a.DetailColor == b.DetailColor
            && SameRows(a.Rows, b.Rows);

        private static bool SameRows(IReadOnlyList<CounterRow>? a, IReadOnlyList<CounterRow>? b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i].IconKey, b[i].IconKey, StringComparison.Ordinal)
                    || !string.Equals(a[i].Value, b[i].Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class RowSlot
        {
            public RowSlot(Image logo, CurvedTextMeshPro badge, CurvedTextMeshPro text)
            {
                Logo = logo;
                Badge = badge;
                Text = text;
            }

            public Image Logo { get; }

            public CurvedTextMeshPro Badge { get; }

            public CurvedTextMeshPro Text { get; }

            public Material? LogoMaterial { get; set; }

            public Vector2 TextSize { get; set; }

            public Vector2 LogoSize { get; set; }

            public MeasureCache<Vector2> TextMeasure { get; } = new MeasureCache<Vector2>();

            public MeasureCache<Vector2> BadgeMeasure { get; } = new MeasureCache<Vector2>();
        }

        private readonly struct FollowMemo
        {
            private readonly Vec3 _position;
            private readonly Vec3 _rotation;
            private readonly float _scale;
            private readonly Vec3 _stayPosition;
            private readonly Vec3 _stayRotation;
            private readonly float _stayScale;

            public FollowMemo(CounterSettings followed)
                : this(followed.Position, followed.Rotation, followed.Scale, default, default, float.NaN)
            {
            }

            private FollowMemo(Vec3 position, Vec3 rotation, float scale, Vec3 stayPosition, Vec3 stayRotation, float stayScale)
            {
                _position = position;
                _rotation = rotation;
                _scale = scale;
                _stayPosition = stayPosition;
                _stayRotation = stayRotation;
                _stayScale = stayScale;
            }

            public FollowMemo WithStaysPutPose(CounterSettings staysPut) =>
                new FollowMemo(_position, _rotation, _scale, staysPut.Position, staysPut.Rotation, staysPut.Scale);

            public bool StaysAt(CounterSettings settings) =>
                settings.Attachment == CounterAttachment.None
                && settings.Position == _stayPosition
                && settings.Rotation == _stayRotation
                && settings.Scale.Equals(_stayScale);

            public void Restore(CounterSettings settings, CounterAttachment attachment)
            {
                settings.Attachment = attachment;
                settings.Position = _position;
                settings.Rotation = _rotation;
                settings.Scale = _scale;
            }
        }
    }
}
