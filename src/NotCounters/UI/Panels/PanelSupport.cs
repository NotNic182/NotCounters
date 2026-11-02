using System;
using System.IO;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.FloatingScreen;
using HMUI;
using NotCounters.Util;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRUIControls;
using Zenject;
using Object = UnityEngine.Object;

namespace NotCounters.UI.Panels
{
    internal static class PanelSupport
    {
        private const int SortingOrder = 0;

        private const string HintControllerName = "NotCountersHoverHints";

        private static readonly FieldInfo? HintControllerField =
            typeof(HoverHint).GetField("_hoverHintController", BindingFlags.Instance | BindingFlags.NonPublic);

        public static BSMLParser? TryGetParser(string panelName)
        {
            try
            {
                return BSMLParser.Instance;
            }
            catch (Exception ex)
            {
                Safe.WarnOnce("PanelSupport.Parser", $"NotCounters: BSML is not available for the {panelName} ({ex.Message}).");
                return null;
            }
        }

        public static string ReadView(string resourceName)
        {
            using var stream = typeof(PanelSupport).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded view '{resourceName}' was not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static FloatingScreen CreateScreen(string name, Vector2 size, float unitsToMetres, float curvatureRadius)
        {
            var screen = FloatingScreen.CreateFloatingScreen(size, false, Vector3.zero, Quaternion.identity, curvatureRadius, false);
            try
            {
                var go = screen.gameObject;
                go.name = name;
                screen.transform.localScale = Vector3.one * unitsToMetres;

                if (screen.Handle != null)
                {
                    Object.Destroy(screen.Handle);
                    screen.Handle = null;
                }

                var canvas = screen.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = SortingOrder;

                var mask = screen.GetComponent<RectMask2D>();
                if (mask != null)
                {
                    Object.DestroyImmediate(mask);
                }

                var background = new GameObject("RaycastBackground", typeof(RectTransform)) { layer = go.layer };
                var rect = (RectTransform)background.transform;
                rect.SetParent(screen.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
                background.AddComponent<Touchable>();
                return screen;
            }
            catch
            {
                Object.Destroy(screen.gameObject);
                throw;
            }
        }

        public static HoverHintController? CreateHintController(FloatingScreen screen)
        {
            GameObject? go = null;
            try
            {
                var prefab = FindHintPanelPrefab();
                if (prefab == null || HintControllerField == null)
                {
                    var missing = prefab == null ? "the game's hover hint bubble" : "the hover hints' controller field";
                    Safe.WarnOnce("PanelSupport.Hints", $"NotCounters: {missing} was not found; the side panels show no hover hints.");
                    return null;
                }

                go = new GameObject(HintControllerName) { layer = screen.gameObject.layer };
                go.SetActive(false);
                go.transform.SetParent(screen.transform, false);
                var controller = go.AddComponent<HoverHintController>();

                controller._hoverHintPanelPrefab = prefab;
                go.SetActive(true);

                if (go.activeInHierarchy && controller._hoverHintPanel == null)
                {
                    throw new InvalidOperationException("the hint bubble was not created");
                }

                SecureHints(controller);
                return controller;
            }
            catch (Exception ex)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }

                Safe.WarnOnce("PanelSupport.Hints", $"NotCounters: the side panels show no hover hints ({ex.Message}).");
                return null;
            }
        }

        public static void SecureHints(HoverHintController hints)
        {
            foreach (var graphic in hints.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            foreach (var raycaster in hints.GetComponentsInChildren<BaseRaycaster>(true))
            {
                raycaster.enabled = false;
            }
        }

        public static void PrepareForGameplay(GameObject root, DiContainer container, HoverHintController? hints)
        {
            foreach (var hint in root.GetComponentsInChildren<HoverHint>(true))
            {
                if (!TryRetarget(hint, hints))
                {
                    Object.Destroy(hint);
                }
            }

            foreach (var localized in root.GetComponentsInChildren<LocalizedHoverHint>(true))
            {
                Object.Destroy(localized);
            }

            foreach (var modal in root.GetComponentsInChildren<ModalView>(true))
            {
                if (modal.GetComponent<VRGraphicRaycaster>() == null)
                {
                    container.InstantiateComponent<VRGraphicRaycaster>(modal.gameObject);
                }
            }

            foreach (var raycaster in root.GetComponentsInChildren<VRGraphicRaycaster>(true))
            {
                raycaster._blockingMask = 0;
            }
        }

        private static HoverHintPanel? FindHintPanelPrefab()
        {
            var menu = BeatSaberUI.DiContainer?.TryResolve<HoverHintController>();
            if (menu != null && menu._hoverHintPanelPrefab != null)
            {
                return menu._hoverHintPanelPrefab;
            }

            foreach (var candidate in Resources.FindObjectsOfTypeAll<HoverHintController>())
            {
                if (candidate != null && candidate.gameObject.scene.IsValid() && candidate.name != HintControllerName
                    && candidate._hoverHintPanelPrefab != null)
                {
                    return candidate._hoverHintPanelPrefab;
                }
            }

            return null;
        }

        private static bool TryRetarget(HoverHint hint, HoverHintController? hints)
        {
            if (hints == null
                || HintControllerField == null
                || string.IsNullOrEmpty(hint.text)
                || hint.GetComponent<LocalizedHoverHint>() != null
                || hint.GetComponentInParent<ModalView>(true) != null)
            {
                return false;
            }

            try
            {
                HintControllerField.SetValue(hint, hints);
                return true;
            }
            catch (Exception ex)
            {
                Safe.WarnOnce("PanelSupport.HintTarget", $"NotCounters: a hover hint could not be connected to its panel and was removed ({ex.Message}).");
                return false;
            }
        }

        public static bool IsClosing(ModalView? modal) => modal != null && !modal.isShown;

        public static void CloseModals(GameObject root)
        {
            foreach (var dropdown in root.GetComponentsInChildren<DropdownWithTableView>(true))
            {
                dropdown.Hide(animated: false);
            }

            foreach (var modal in root.GetComponentsInChildren<ModalView>(true))
            {
                if (modal.isShown)
                {
                    modal.Hide(animated: false);
                }
            }

            FinishDismissals(root);
        }

        public static void FinishDismissals(GameObject root)
        {
            foreach (var modal in root.GetComponentsInChildren<ModalView>(true))
            {
                FinishDismissal(modal);
            }
        }

        public static bool FinishDismissal(ModalView? modal)
        {
            if (modal == null || modal.isShown || !modal.gameObject.activeSelf)
            {
                return false;
            }

            var animation = modal.GetComponent<PanelAnimation>();
            if (animation != null)
            {
                Object.DestroyImmediate(animation);
            }

            var previousParent = modal._previousParent;
            if (previousParent != null && modal.transform.parent != previousParent)
            {
                modal.transform.SetParent(previousParent, worldPositionStays: true);
            }

            modal.gameObject.SetActive(false);
            return true;
        }

        public static void RestoreDimming(GameObject root)
        {
            var group = root.GetComponent<CanvasGroup>();
            if (group == null || Mathf.Approximately(group.alpha, 1f))
            {
                return;
            }

            foreach (var modal in root.GetComponentsInChildren<ModalView>(true))
            {
                if (modal.isShown)
                {
                    return;
                }
            }

            group.alpha = 1f;
        }

        public static void SetButton(Button? button, bool interactable, string? text = null)
        {
            if (button == null)
            {
                return;
            }

            if (button.interactable != interactable)
            {
                button.interactable = interactable;
            }

            if (text != null)
            {
                var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null && label.text != text)
                {
                    label.text = text;
                }
            }
        }

        public static void SetText(TextMeshProUGUI? label, string text)
        {
            if (label != null && label.text != text)
            {
                label.text = text;
            }
        }

        public static void SetActive(GameObject? go, bool active)
        {
            if (go != null && go.activeSelf != active)
            {
                go.SetActive(active);
            }
        }
    }
}
