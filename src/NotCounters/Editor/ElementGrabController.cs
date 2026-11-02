using System;
using System.Collections.Generic;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Util;
using SiraUtil.Logging;
using SiraUtil.Tools.FPFC;
using UnityEngine;
using UnityEngine.EventSystems;
using VRUIControls;
using Zenject;
using Object = UnityEngine.Object;

namespace NotCounters.Editor
{
    internal sealed class ElementGrabController : IInitializable, ILateTickable, IDisposable, IEditHandleListener
    {
        private const float ReleaseTriggerThreshold = 0.5f;

        private const float MinGrabDistance = 0.15f;
        private const float MaxGrabDistance = 25f;

        private const float ClickPositionTolerance = 0.005f;
        private const float ClickAngleTolerance = 0.5f;

        private const float WheelDistanceFactor = 1.08f;
        private const float WheelScaleFactor = 1.05f;
        private const float WheelYawDegrees = 5f;
        private const float NudgeStep = 0.01f;
        private const float NudgeStepLarge = 0.1f;

        private readonly SiraLog _log;
        private readonly DiContainer _container;
        private readonly IConfigService _config;
        private readonly EditSessionController _session;
        private readonly EditSelection _selection;
        private readonly EditPlayHints _hints;
        private readonly EditorClickSound _clickSound;
        private readonly IEditableElementRegistry? _registry;
        private readonly IHudAnchorProvider? _anchorProvider;
        private readonly VRInputModule? _inputModule;
        private readonly PauseMenuManager? _pauseMenuManager;
        private readonly IFPFCSettings? _fpfcSettings;
        private readonly MainCamera? _mainCamera;

        private readonly Dictionary<IEditableElement, EditFrame> _frames = new Dictionary<IEditableElement, EditFrame>();
        private readonly List<IEditableElement> _staleElements = new List<IEditableElement>();
        private readonly HashSet<IEditableElement> _currentElements = new HashSet<IEditableElement>();

        private readonly Action _updateGrab;
        private readonly Action _refreshFrames;

        private GrabState? _grab;
        private GameObject? _fallbackAnchor;
        private bool _legacyInputUnavailable;

        public ElementGrabController(
            SiraLog log,
            DiContainer container,
            IConfigService config,
            EditSessionController session,
            EditSelection selection,
            EditPlayHints hints,
            EditorClickSound clickSound,
            [InjectOptional] IEditableElementRegistry? registry,
            [InjectOptional] IHudAnchorProvider? anchorProvider,
            [InjectOptional] VRInputModule? inputModule,
            [InjectOptional] PauseMenuManager? pauseMenuManager,
            [InjectOptional] IFPFCSettings? fpfcSettings,
            [InjectOptional] MainCamera? mainCamera)
        {
            _log = log;
            _container = container;
            _config = config;
            _session = session;
            _selection = selection;
            _hints = hints;
            _clickSound = clickSound;
            _registry = registry;
            _anchorProvider = anchorProvider;
            _inputModule = inputModule;
            _pauseMenuManager = pauseMenuManager;
            _fpfcSettings = fpfcSettings;
            _mainCamera = mainCamera;
            _updateGrab = UpdateGrab;
            _refreshFrames = RefreshFrames;
        }

        private EditorSettings Settings => _config.Config.Global.Editor;

        public void Initialize()
        {
            _session.EditingStateChanged += HandleEditingStateChanged;
            if (_registry != null)
            {
                _registry.ElementsChanged += HandleElementsChanged;
            }
            else
            {
                _log.Warn("No IEditableElementRegistry is bound; nothing can be dragged in this edit session.");
            }

            if (_fpfcSettings != null)
            {
                _fpfcSettings.Changed += HandleFpfcChanged;
            }
        }

        public void LateTick()
        {
            if (!_session.IsEditing)
            {
                return;
            }

            Safe.Invoke("ElementGrabController.UpdateGrab", _updateGrab);
            if (!_legacyInputUnavailable)
            {
                try
                {
                    HandleKeyboard();
                }
                catch (InvalidOperationException ex)
                {
                    DisableLegacyInput(ex);
                }
                catch (Exception ex)
                {
                    Safe.Report("ElementGrabController.Keyboard", ex);
                }
            }

            Safe.Invoke("ElementGrabController.RefreshFrames", _refreshFrames);
        }

        public void Dispose()
        {
            Safe.Invoke("ElementGrabController.Dispose", () =>
            {
                _session.EditingStateChanged -= HandleEditingStateChanged;
                if (_registry != null)
                {
                    _registry.ElementsChanged -= HandleElementsChanged;
                }

                if (_fpfcSettings != null)
                {
                    _fpfcSettings.Changed -= HandleFpfcChanged;
                }

                EndGrab(commit: false);
                DestroyFrames();
                if (_fallbackAnchor != null)
                {
                    Object.Destroy(_fallbackAnchor);
                    _fallbackAnchor = null;
                }
            });
        }

        public bool CanCommandSelected => SelectedForCommand() != null;

        public void ResetSelected()
        {
            var element = SelectedForCommand();
            if (element != null)
            {
                _session.RecordEdit(element.ResetToDefault);
            }
        }

        void IEditHandleListener.OnHandlePointerEnter(EditFrame frame) => frame.Hovered = true;

        void IEditHandleListener.OnHandlePointerExit(EditFrame frame) => frame.Hovered = false;

        void IEditHandleListener.OnHandlePointerDown(EditFrame frame, PointerEventData eventData)
        {
            if (!_session.IsEditing || !frame.IsAlive || !IsCurrentFrame(frame))
            {
                return;
            }

            _clickSound.Play();
            _selection.Select(frame.Element);
            if (_grab == null)
            {
                BeginGrab(frame);
            }
        }

        void IEditHandleListener.OnHandlePointerUp(EditFrame frame, PointerEventData eventData)
        {
            if (_grab != null && ReferenceEquals(_grab.Frame, frame))
            {
                EndGrab(commit: true);
            }
        }

        private Transform Anchor
        {
            get
            {
                var anchor = EditorAnchor.TryGet(_anchorProvider);
                if (anchor != null)
                {
                    return anchor;
                }

                if (_fallbackAnchor == null)
                {
                    _fallbackAnchor = new GameObject("NotCountersEditorFallbackAnchor");
                }

                return _fallbackAnchor.transform;
            }
        }

        private Transform? Head
        {
            get
            {
                if (_mainCamera != null)
                {
                    return _mainCamera.transform;
                }

                var camera = Camera.main;
                return camera != null ? camera.transform : null;
            }
        }

        private void BeginGrab(EditFrame frame)
        {
            var controller = ResolvePointerController();
            if (controller == null)
            {
                Safe.WarnOnce("ElementGrabController.NoController", "No active pointer controller; HUD elements can be selected but not dragged.");
                return;
            }

            var anchor = Anchor;
            var hand = ControllerTransform(controller);
            var pose = frame.Element.GetPose();
            var worldPosition = anchor.TransformPoint(pose.Position);

            _grab = new GrabState(frame, controller, pose, Settings.TurnWithController)
            {
                Offset = Quaternion.Inverse(hand.rotation) * (worldPosition - hand.position),
                StartWorldRotation = anchor.rotation * pose.Rotation,
                LastControllerYaw = EditPoseMath.YawAround(anchor, hand.forward) ?? 0f,
                Scale = EditPoseMath.FiniteScale(pose.Scale),
                LastPose = pose,
            };
            frame.Grabbed = true;
        }

        private void UpdateGrab()
        {
            var grab = _grab;
            if (grab == null)
            {
                return;
            }

            if (!grab.Frame.IsAlive)
            {
                _grab = null;
                return;
            }

            var controller = grab.Controller;
            if (controller == null || !controller.active || controller.triggerValue < ReleaseTriggerThreshold)
            {
                EndGrab(commit: true);
                return;
            }

            if (IsDesktop(controller))
            {
                TryApplyMouseWheel(grab);
            }
            else
            {
                ApplyThumbstick(grab, controller, Time.unscaledDeltaTime);
            }

            var anchor = Anchor;
            var hand = ControllerTransform(controller);
            var worldPosition = hand.position + (hand.rotation * grab.Offset);
            var pose = new AnchorPose(anchor.InverseTransformPoint(worldPosition), DraggedRotation(grab, anchor, hand), grab.Scale);

            if (!EditPoseMath.Differs(pose, grab.LastPose) && !EditPoseMath.Differs(grab.Frame.Element.GetPose(), grab.LastPose))
            {
                return;
            }

            grab.Frame.Element.SetPose(pose);
            grab.LastPose = pose;
            grab.Moved |= (pose.Position - grab.StartPose.Position).magnitude > ClickPositionTolerance
                || Quaternion.Angle(pose.Rotation, grab.StartPose.Rotation) > ClickAngleTolerance
                || Math.Abs(pose.Scale - grab.StartPose.Scale) > 0.001f;
        }

        private static Quaternion DraggedRotation(GrabState grab, Transform anchor, Transform hand)
        {
            if (!grab.TurnWithController)
            {
                return Quaternion.AngleAxis(grab.ExtraYaw, Vector3.up) * grab.StartPose.Rotation;
            }

            var yaw = EditPoseMath.YawAround(anchor, hand.forward);
            if (yaw.HasValue)
            {
                grab.AccumulatedYaw += Mathf.DeltaAngle(grab.LastControllerYaw, yaw.Value);
                grab.LastControllerYaw = yaw.Value;
            }

            var worldRotation = Quaternion.AngleAxis(grab.AccumulatedYaw + grab.ExtraYaw, anchor.up) * grab.StartWorldRotation;
            return Quaternion.Inverse(anchor.rotation) * worldRotation;
        }

        private void ApplyThumbstick(GrabState grab, VRController controller, float deltaTime)
        {
            var settings = Settings;

            var stick = controller.thumbstick;
            var pushPull = EditPoseMath.ApplyDeadzone(stick.y, settings.ThumbstickDeadzone);
            if (settings.InvertThumbstickY)
            {
                pushPull = -pushPull;
            }

            var scaling = EditPoseMath.ApplyDeadzone(stick.x, settings.ThumbstickDeadzone);

            if (pushPull != 0f)
            {
                var offset = grab.Offset;
                offset.z = Mathf.Clamp(offset.z + (pushPull * settings.PushPullSpeed * deltaTime), MinGrabDistance, MaxGrabDistance);
                grab.Offset = offset;
            }

            if (scaling != 0f)
            {
                grab.Scale = EditPoseMath.ClampScale(grab.Scale * Mathf.Exp(scaling * settings.ScaleSpeed * deltaTime));
            }
        }

        private void TryApplyMouseWheel(GrabState grab)
        {
            if (_legacyInputUnavailable)
            {
                return;
            }

            try
            {
                ApplyMouseWheel(grab);
            }
            catch (InvalidOperationException ex)
            {
                DisableLegacyInput(ex);
            }
        }

        private void DisableLegacyInput(InvalidOperationException ex)
        {
            _legacyInputUnavailable = true;
            _log.Warn($"Legacy keyboard/mouse input is unavailable; desktop editor shortcuts are disabled: {ex.Message}");
        }

        private void ApplyMouseWheel(GrabState grab)
        {
            var wheel = Input.mouseScrollDelta.y;
            if (Math.Abs(wheel) < 0.01f)
            {
                return;
            }

            if (IsShiftHeld())
            {
                grab.Scale = EditPoseMath.ClampScale(grab.Scale * Mathf.Pow(WheelScaleFactor, wheel));
            }
            else if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                grab.ExtraYaw += wheel * WheelYawDegrees;
            }
            else
            {
                var offset = grab.Offset;
                offset.z = Mathf.Clamp(Math.Max(offset.z, MinGrabDistance) * Mathf.Pow(WheelDistanceFactor, wheel), MinGrabDistance, MaxGrabDistance);
                grab.Offset = offset;
            }
        }

        private void EndGrab(bool commit)
        {
            var grab = _grab;
            if (grab == null)
            {
                return;
            }

            _grab = null;
            grab.Frame.Grabbed = false;
            if (!grab.Frame.IsAlive)
            {
                return;
            }

            var element = grab.Frame.Element;
            if (!commit || !grab.Moved)
            {
                element.SetPose(grab.StartPose);
                return;
            }

            var pose = grab.LastPose;
            var settings = Settings;

            var rotationChanged = Quaternion.Angle(pose.Rotation, grab.StartPose.Rotation) > ClickAngleTolerance;
            if (!rotationChanged)
            {
                pose.Rotation = grab.StartPose.Rotation;
            }

            if (settings.FaceHeadOnRelease)
            {
                var head = Head;
                if (head != null)
                {
                    var anchor = Anchor;
                    pose.Rotation = EditPoseMath.FaceHead(anchor, anchor.TransformPoint(pose.Position), head.position, pose.Rotation);
                    rotationChanged = true;
                }
            }

            if (settings.SnapToGrid)
            {
                pose = EditPoseMath.Snap(pose, settings.GridSize, settings.AngleSnapDegrees, snapRotation: rotationChanged);
            }

            element.SetPose(pose);
            _session.RecordEdit(element.CommitToConfig);
        }

        private void HandleKeyboard()
        {
            if (_grab != null || !Input.anyKeyDown)
            {
                return;
            }

            var element = _selection.Selected;
            if (element == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Home))
            {
                ResetSelected();
                return;
            }

            var direction = Vector3.zero;
            NudgeAxes(out var right, out var up, out var forward);
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                direction -= right;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                direction += right;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                direction += up;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                direction -= up;
            }

            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                direction += forward;
            }

            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                direction -= forward;
            }

            if (direction == Vector3.zero)
            {
                return;
            }

            var pose = element.GetPose();
            pose.Position += direction * (IsShiftHeld() ? NudgeStepLarge : NudgeStep);
            element.SetPose(pose);
            _session.RecordEdit(element.CommitToConfig, "nudge:" + element.Id);
        }

        private void NudgeAxes(out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            up = Vector3.up;
            forward = Vector3.forward;
            var head = Head;
            if (head != null)
            {
                var local = Anchor.InverseTransformDirection(head.forward);
                local.y = 0f;
                if (local.sqrMagnitude > 1e-4f)
                {
                    forward = local.normalized;
                }
            }

            right = Vector3.Cross(up, forward);
        }

        private void RefreshFrames()
        {
            _staleElements.Clear();

            var selectedId = _selection.Selected?.Id;
            foreach (var pair in _frames)
            {
                try
                {
                    var frame = pair.Value;
                    if (!frame.IsAlive)
                    {
                        _staleElements.Add(pair.Key);
                        continue;
                    }

                    frame.Selected = selectedId != null && string.Equals(pair.Key.Id, selectedId, StringComparison.Ordinal);
                    frame.HiddenInPlay = _hints.IsHiddenInPlay(pair.Key);
                    frame.Refresh();
                }
                catch (Exception ex)
                {
                    Safe.Report("ElementGrabController.RefreshFrame", ex);
                }
            }

            foreach (var element in _staleElements)
            {
                RemoveFrame(element);
            }
        }

        private void SyncFrames()
        {
            if (!_session.IsEditing || _registry == null)
            {
                return;
            }

            _currentElements.Clear();
            foreach (var element in _registry.Elements)
            {
                if (element == null || element.Root == null)
                {
                    continue;
                }

                _currentElements.Add(element);
                if (!_frames.ContainsKey(element))
                {
                    var created = Safe.Invoke<EditFrame?>("ElementGrabController.CreateFrame", () => new EditFrame(element, _container, this), null);
                    if (created != null)
                    {
                        _frames.Add(element, created);
                    }
                }
            }

            _staleElements.Clear();
            foreach (var element in _frames.Keys)
            {
                if (!_currentElements.Contains(element))
                {
                    _staleElements.Add(element);
                }
            }

            foreach (var element in _staleElements)
            {
                RemoveFrame(element);
            }
        }

        private void RemoveFrame(IEditableElement element)
        {
            if (!_frames.TryGetValue(element, out var frame))
            {
                return;
            }

            if (_grab != null && ReferenceEquals(_grab.Frame, frame))
            {
                EndGrab(commit: false);
            }

            frame.Destroy();
            _frames.Remove(element);
        }

        private bool IsCurrentFrame(EditFrame frame) =>
            _frames.TryGetValue(frame.Element, out var current) && ReferenceEquals(current, frame);

        private void DestroyFrames()
        {
            foreach (var frame in _frames.Values)
            {
                frame.Destroy();
            }

            _frames.Clear();
        }

        private VRController? ResolvePointerController()
        {
            VRPointer? pointer = null;
            if (_inputModule != null && _inputModule._vrPointer != null)
            {
                pointer = _inputModule._vrPointer;
            }
            else if (_pauseMenuManager != null && _pauseMenuManager._vrPointer != null)
            {
                pointer = _pauseMenuManager._vrPointer;
            }

            var controller = pointer != null ? pointer.lastSelectedVrController : null;
            return controller != null && controller.active ? controller : null;
        }

        private static Transform ControllerTransform(VRController controller) =>
            controller.viewAnchorTransform != null ? controller.viewAnchorTransform : controller.transform;

        private bool IsDesktop(VRController controller) => (_fpfcSettings != null && _fpfcSettings.Enabled) || controller.mouseMode;

        private static bool IsShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        private IEditableElement? SelectedForCommand() => _session.IsEditing && _grab == null ? _selection.Selected : null;

        private void HandleEditingStateChanged() => Safe.Invoke("ElementGrabController.EditingStateChanged", () =>
        {
            if (_session.IsEditing)
            {
                SyncFrames();
                _selection.EnsureSelection();
            }
            else
            {
                EndGrab(commit: false);
                DestroyFrames();
            }
        });

        private void HandleElementsChanged() => Safe.Invoke("ElementGrabController.ElementsChanged", () =>
        {
            if (_session.IsEditing)
            {
                SyncFrames();
                _selection.EnsureSelection();
            }
        });

        private void HandleFpfcChanged(IFPFCSettings settings) =>
            Safe.Invoke("ElementGrabController.FpfcChanged", () => EndGrab(commit: false));

        private sealed class GrabState
        {
            public GrabState(EditFrame frame, VRController controller, AnchorPose startPose, bool turnWithController)
            {
                Frame = frame;
                Controller = controller;
                StartPose = startPose;
                TurnWithController = turnWithController;
            }

            public EditFrame Frame { get; }

            public VRController Controller { get; }

            public AnchorPose StartPose { get; }

            public bool TurnWithController { get; }

            public Vector3 Offset { get; set; }

            public Quaternion StartWorldRotation { get; set; }

            public float LastControllerYaw { get; set; }

            public float AccumulatedYaw { get; set; }

            public float ExtraYaw { get; set; }

            public float Scale { get; set; }

            public AnchorPose LastPose { get; set; }

            public bool Moved { get; set; }
        }
    }
}
