using System;
using NotCounters.Contracts;
using NotCounters.Core.Editing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using Object = UnityEngine.Object;

namespace NotCounters.Editor
{
    internal sealed class EditFrame
    {
        private const float PaddingMetres = 0.03f;
        private const float PaddingFraction = 0.04f;

        private const float MinContentMetres = 0.08f;

        private const float DepthOffsetMetres = 0.002f;

        private const int SortingOrder = -3;

        private const float ScaleEpsilon = 1e-6f;

        private static readonly Color VanillaTint = new Color(0.30f, 0.78f, 1f);
        private static readonly Color CounterTint = new Color(0.72f, 0.48f, 1f);
        private static readonly Color HiddenTint = new Color(0.62f, 0.62f, 0.66f);

        private readonly RectTransform _canvasRect;
        private readonly Image _fill;
        private readonly Image[] _edges;
        private readonly TextMeshProUGUI _label;

        private readonly EditFrameLabel _labelText = new EditFrameLabel();

        private Vector2 _appliedSize = new Vector2(float.NaN, float.NaN);
        private Vector3 _appliedScale = new Vector3(float.NaN, float.NaN, float.NaN);
        private Vector3 _appliedPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        private float _appliedEdgeThickness = float.NaN;
        private Color _appliedFill;
        private Color _appliedEdge;
        private string? _appliedLabel;
        private bool _appliedLabelVisible;

        public EditFrame(IEditableElement element, DiContainer container, IEditHandleListener listener)
        {
            Element = element;

            _canvasRect = EditorUi.CreateRect("NotCountersEditFrame", element.Root);
            var go = _canvasRect.gameObject;
            go.SetActive(false);
            _canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
            _canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            _canvasRect.pivot = new Vector2(0.5f, 0.5f);
            _canvasRect.localRotation = Quaternion.identity;
            EditorUi.AddInteractiveCanvas(go, SortingOrder, container);

            _fill = EditorUi.CreateImage("Fill", _canvasRect, Color.clear, raycastTarget: true);
            EditorUi.Stretch(_fill.rectTransform, 0f);
            _fill.gameObject.AddComponent<EditHandle>().Bind(this, listener);

            _edges = new Image[4];
            for (var i = 0; i < _edges.Length; i++)
            {
                _edges[i] = EditorUi.CreateImage("Edge" + i, _canvasRect, Color.clear, raycastTarget: false);
            }

            _label = EditorUi.CreateText("Name", _canvasRect, string.Empty, 40f, TextAlignmentOptions.BottomLeft, Color.white);
            _label.rectTransform.anchorMin = new Vector2(0f, 1f);
            _label.rectTransform.anchorMax = new Vector2(0f, 1f);
            _label.rectTransform.pivot = new Vector2(0f, 0f);
            _label.gameObject.SetActive(false);

            Refresh();
            go.SetActive(true);
        }

        public IEditableElement Element { get; }

        public bool IsAlive => _canvasRect != null && Element.Root != null;

        public bool Hovered { get; set; }

        public bool Selected { get; set; }

        public bool Grabbed { get; set; }

        public bool HiddenInPlay { get; set; }

        public void Refresh()
        {
            var target = Element.Root;
            if (target == null || _canvasRect == null)
            {
                return;
            }

            var lossy = target.lossyScale;
            if (Math.Abs(lossy.x) < ScaleEpsilon || Math.Abs(lossy.y) < ScaleEpsilon)
            {
                return;
            }

            var depthScale = Math.Abs(lossy.z) < ScaleEpsilon ? 1f : lossy.z;
            var content = Element.GetWorldSize();
            var contentWidth = Math.Max(MinContentMetres, content.x);
            var contentHeight = Math.Max(MinContentMetres, content.y);
            var padding = PaddingMetres + (PaddingFraction * Math.Max(contentWidth, contentHeight));
            var size = new Vector2(contentWidth + (2f * padding), contentHeight + (2f * padding)) * EditorUi.UnitsPerMetre;

            var scale = new Vector3(
                1f / (EditorUi.UnitsPerMetre * lossy.x),
                1f / (EditorUi.UnitsPerMetre * lossy.y),
                1f / (EditorUi.UnitsPerMetre * depthScale));

            var centre = Element.GetLocalContentCentre();
            var position = new Vector3(centre.x, centre.y, DepthOffsetMetres / depthScale);

            if (size != _appliedSize)
            {
                _canvasRect.sizeDelta = size;
                _appliedSize = size;
            }

            if (scale != _appliedScale)
            {
                _canvasRect.localScale = scale;
                _appliedScale = scale;
            }

            if (position != _appliedPosition)
            {
                _canvasRect.localPosition = position;
                _canvasRect.localRotation = Quaternion.identity;
                _appliedPosition = position;
            }

            RefreshVisuals(size);
        }

        public void Destroy()
        {
            if (_canvasRect != null)
            {
                Object.Destroy(_canvasRect.gameObject);
            }
        }

        private void RefreshVisuals(Vector2 size)
        {
            var visible = Element.IsVisible;
            var tint = !visible || HiddenInPlay ? HiddenTint : Element.Kind == EditableElementKind.Vanilla ? VanillaTint : CounterTint;

            float fillAlpha;
            float edgeAlpha;
            if (Grabbed)
            {
                fillAlpha = 0.22f;
                edgeAlpha = 1f;
            }
            else if (Hovered)
            {
                fillAlpha = 0.16f;
                edgeAlpha = 0.7f;
            }
            else if (Selected)
            {
                fillAlpha = 0.12f;
                edgeAlpha = 1f;
            }
            else
            {
                fillAlpha = 0.08f;
                edgeAlpha = 0.35f;
            }

            var fill = new Color(tint.r, tint.g, tint.b, fillAlpha);
            if (fill != _appliedFill)
            {
                _fill.color = fill;
                _appliedFill = fill;
            }

            var edge = new Color(tint.r, tint.g, tint.b, edgeAlpha);
            var thickness = Mathf.Clamp(Math.Min(size.x, size.y) * 0.02f, 5f, 30f) * (Selected || Grabbed ? 2f : 1f);
            if (edge != _appliedEdge || !Mathf.Approximately(thickness, _appliedEdgeThickness))
            {
                LayoutEdges(thickness, edge);
                _appliedEdge = edge;
                _appliedEdgeThickness = thickness;
            }

            var labelVisible = Hovered || Selected || Grabbed;
            if (labelVisible != _appliedLabelVisible)
            {
                _label.gameObject.SetActive(labelVisible);
                _appliedLabelVisible = labelVisible;
            }

            if (labelVisible)
            {
                var text = _labelText.Text(Element.DisplayName, Element.Kind == EditableElementKind.Counter, visible, HiddenInPlay);
                var fontSize = Mathf.Clamp(size.y * 0.2f, 30f, 160f);
                if (text != _appliedLabel || !Mathf.Approximately(_label.fontSize, fontSize))
                {
                    _label.text = text;
                    _label.fontSize = fontSize;
                    _label.color = new Color(tint.r, tint.g, tint.b, 1f);
                    _label.rectTransform.sizeDelta = new Vector2(Math.Max(size.x, fontSize * Math.Max(12f, text.Length * 0.6f)), fontSize * 1.3f);
                    _appliedLabel = text;
                }

                var labelOffset = new Vector2(0f, thickness + 4f);
                if (_label.rectTransform.anchoredPosition != labelOffset)
                {
                    _label.rectTransform.anchoredPosition = labelOffset;
                }
            }
        }

        private void LayoutEdges(float thickness, Color color)
        {
            SetEdge(_edges[0], new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, thickness), color);
            SetEdge(_edges[1], new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, thickness), color);
            SetEdge(_edges[2], new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, 0f), color);
            SetEdge(_edges[3], new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(thickness, 0f), color);
        }

        private static void SetEdge(Image edge, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Color color)
        {
            var rect = edge.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            edge.color = color;
        }
    }
}
