using System;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Util;
using SiraUtil.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;
using Object = UnityEngine.Object;

namespace NotCounters.Hud
{
    internal sealed class HudAnchorProvider : IHudAnchorProvider, IInitializable, ILateTickable, IDisposable
    {
        private const string FrameObjectName = "NotCountersAnchor";
        private const string IndependentObjectName = "NotCountersCounterAnchor";

        private const int InverseIterations = 48;

        private const float MinPanelScale = 0.05f;
        private const float MaxPanelScale = 20f;

        private const string GameCoreSceneName = "GameCore";

        private readonly SiraLog _log;
        private readonly IConfigService _config;
        private readonly CoreGameHUDController? _injectedHud;

        private readonly Action _followHudYaw;

        private bool _resolved;
        private CoreGameHUDController? _hud;
        private Transform? _frame;
        private Transform? _independentAnchor;
        private FlyingGameHUDRotation? _hudRotation;

        private PanelPose? _leftPanel;

        private PanelPose? _rightPanel;

        private SideMap _leftMap = SideMap.Identity;
        private SideMap _rightMap = SideMap.Identity;

        private float? _floorHeight;
        private Vector2? _energyBarSpan;

        public HudAnchorProvider(
            SiraLog log,
            IConfigService config,
            [InjectOptional] CoreGameHUDController? hud,
            [InjectOptional] GameplayCoreSceneSetupData? sceneSetupData,
            [InjectOptional] IEditSessionState? editSession)
        {
            _log = log;
            _config = config;

            _injectedHud = hud;

            var environment = sceneSetupData?.targetEnvironmentInfo != null
                ? sceneSetupData.targetEnvironmentInfo.serializedName
                : null;
            if (string.IsNullOrEmpty(environment) && editSession is { IsActive: true })
            {
                environment = editSession.EnvironmentSerializedName;
            }

            EnvironmentSerializedName = environment ?? string.Empty;
            _followHudYaw = FollowHudYaw;
        }

        public Transform Anchor
        {
            get
            {
                EnsureResolved();
                return _independentAnchor != null ? _independentAnchor : _frame!;
            }
        }

        public string EnvironmentSerializedName { get; }

        internal CoreGameHUDController? HudController
        {
            get
            {
                EnsureResolved();
                return _hud;
            }
        }

        internal Transform? VanillaFrame
        {
            get
            {
                EnsureResolved();
                return _frame;
            }
        }

        public float? FloorHeight
        {
            get
            {
                EnsureResolved();
                return _floorHeight;
            }
        }

        public Vector3 EyePosition => new Vector3(0f, HudFollow.EyeHeightMetres, 0f);

        public Vector2? EnergyBarSpan
        {
            get
            {
                EnsureResolved();
                return _energyBarSpan;
            }
        }

        public AnchorPose ToAnchor(CounterAttachment attachment, Vector3 position, Quaternion rotation, float scale)
        {
            EnsureResolved();
            if (attachment == CounterAttachment.None)
            {
                return new AnchorPose(position, rotation, scale);
            }

            var weight = HudFollow.SideWeight(position.x);
            return new AnchorPose(
                Vector3.Lerp(_leftMap.Apply(position), _rightMap.Apply(position), weight),
                Quaternion.Slerp(_leftMap.Rotation, _rightMap.Rotation, weight) * rotation,
                Mathf.Lerp(_leftMap.Scale, _rightMap.Scale, weight) * scale);
        }

        public AnchorPose FromAnchor(CounterAttachment attachment, AnchorPose pose)
        {
            EnsureResolved();
            if (attachment == CounterAttachment.None)
            {
                return pose;
            }

            var target = pose.Position;
            var weight = HudFollow.SideWeight(target.x);
            var estimate = target;
            for (var i = 0; i < InverseIterations; i++)
            {
                var next = SideMap.Solve(_leftMap, _rightMap, weight, target);
                var settled = (next - estimate).sqrMagnitude < 1e-12f;
                estimate = next;
                weight = HudFollow.SideWeight(estimate.x);
                if (settled)
                {
                    break;
                }
            }

            var blendScale = Mathf.Lerp(_leftMap.Scale, _rightMap.Scale, weight);
            return new AnchorPose(
                estimate,
                Quaternion.Inverse(Quaternion.Slerp(_leftMap.Rotation, _rightMap.Rotation, weight)) * pose.Rotation,
                pose.Scale / Mathf.Max(blendScale, 1e-6f));
        }

        public void Initialize() => Safe.Invoke("hud:anchor-init", EnsureResolved);

        public void LateTick() => Safe.Invoke("hud:anchor-late", _followHudYaw);

        public void Dispose()
        {
            Safe.Invoke("hud:anchor-dispose", () =>
            {
                DestroyIfAlive(_independentAnchor);
                DestroyIfAlive(_frame);
                _independentAnchor = null;
                _frame = null;
            });
        }

        private void EnsureResolved()
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;
            _hud = _injectedHud != null ? _injectedHud : FindHudInScene();

            if (_hud != null)
            {
                var root = _hud.transform;
                var frame = CreateFrame(root);
                _frame = frame;

                // GlassDesert has a second one on its lights
                _hudRotation = root.GetComponent<FlyingGameHUDRotation>();

                _leftPanel = Safe.Invoke<PanelPose?>("hud:anchor-left-panel", () => CapturePanel(root.GetComponentInChildren<ComboUIController>(true), frame, SidePanelReference.ElementScale), null);
                _rightPanel = Safe.Invoke<PanelPose?>("hud:anchor-right-panel", () => CapturePanel(root.GetComponentInChildren<ScoreMultiplierUIController>(true), frame, SidePanelReference.ElementScale), null);
                var energy = Safe.Invoke<PanelPose?>("hud:anchor-energy", () => CapturePanel(root.GetComponentInChildren<GameEnergyUIPanel>(true), frame, HudFollow.ReferenceEnergyScale), null);
                _leftMap = SideMap.For(_leftPanel, SidePanelReference.LeftPanelElementPosition);
                _rightMap = SideMap.For(_rightPanel, SidePanelReference.RightPanelElementPosition);

                if (energy is { } bar)
                {
                    var halfHeight = HudFollow.EnergyBarHalfHeightUnits * HudFollow.ReferenceEnergyScale * bar.Scale;
                    _floorHeight = HudFollow.FloorHeight(bar.Position.y, halfHeight);
                    _energyBarSpan = new Vector2(bar.Position.y - halfHeight, bar.Position.y + halfHeight);
                }
                _log.Debug($"HUD mapping: left {Describe(_leftPanel)}, right {Describe(_rightPanel)}, energy {Describe(energy)}, floor line {(_floorHeight.HasValue ? _floorHeight.Value.ToString("0.###") : "open")}.");
            }

            var hideWithHud = _config.Config.Global.HideCountersWhenHudHidden;
            if (_hud == null || !hideWithHud)
            {
                _independentAnchor = CreateIndependentAnchor();
            }

            _log.Debug(_hud != null
                ? $"HUD found ({_hud.name}); counters are {(hideWithHud ? "parented to the HUD" : "on an independent anchor")}. Environment '{EnvironmentSerializedName}'."
                : $"No vanilla HUD in this scene; counters use an independent anchor. Environment '{EnvironmentSerializedName}'.");
        }

        private static CoreGameHUDController? FindHudInScene()
        {
            var candidates = Object.FindObjectsByType<CoreGameHUDController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var candidate in candidates)
            {
                if (candidate != null && candidate.gameObject.scene.isLoaded)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Transform CreateFrame(Transform hudRoot)
        {
            var frame = new GameObject(FrameObjectName) { layer = HudMath.UiLayer }.transform;
            frame.SetParent(hudRoot, worldPositionStays: false);
            PlaceAtPlayerOrigin(frame, hudRoot);

            var rootScale = hudRoot.lossyScale;
            frame.localScale = new Vector3(SafeInverse(rootScale.x), SafeInverse(rootScale.y), SafeInverse(rootScale.z));
            return frame;
        }

        private static PanelPose? CapturePanel(Component? element, Transform frame, float referenceScale)
        {
            if (element == null)
            {
                return null;
            }

            var t = element.transform;

            var frameScale = Mathf.Abs(frame.lossyScale.x);
            var scale = Mathf.Abs(t.lossyScale.x) / (Mathf.Max(frameScale, 1e-6f) * referenceScale);
            if (!(scale >= MinPanelScale && scale <= MaxPanelScale))
            {
                scale = 1f;
            }

            return new PanelPose(frame.InverseTransformPoint(t.position), Quaternion.Inverse(frame.rotation) * t.rotation, scale);
        }

        private static string Describe(PanelPose? pose) =>
            pose is { } p ? $"{p.Position} x{p.Scale:0.###} {p.Rotation.eulerAngles}" : "none";

        private static Transform CreateIndependentAnchor()
        {
            var go = new GameObject(IndependentObjectName) { layer = HudMath.UiLayer };
            var scene = SceneManager.GetSceneByName(GameCoreSceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            var anchor = go.transform;
            anchor.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return anchor;
        }

        private void FollowHudYaw()
        {
            if (_hud == null)
            {
                return;
            }

            var root = _hud.transform;

            if (_frame != null && _hudRotation != null && _hudRotation.isActiveAndEnabled)
            {
                PlaceAtPlayerOrigin(_frame, root);
            }

            if (_independentAnchor != null)
            {
                _independentAnchor.SetPositionAndRotation(Vector3.zero, YawOf(root));
            }
        }

        private static void PlaceAtPlayerOrigin(Transform frame, Transform hudRoot) =>
            frame.SetPositionAndRotation(Vector3.zero, YawOf(hudRoot));

        private static Quaternion YawOf(Transform t) => Quaternion.Euler(0f, t.eulerAngles.y, 0f);

        private static float SafeInverse(float value) => Mathf.Abs(value) < 1e-6f ? 1f : 1f / value;

        private static void DestroyIfAlive(Transform? t)
        {
            if (t != null)
            {
                Object.Destroy(t.gameObject);
            }
        }

        private readonly struct SideMap
        {
            public static readonly SideMap Identity = new SideMap(Vector3.zero, Quaternion.identity, 1f);

            private SideMap(Vector3 offset, Quaternion rotation, float scale)
            {
                Offset = offset;
                Rotation = rotation;
                Scale = scale;
                Linear = Matrix4x4.TRS(Vector3.zero, rotation, Vector3.one * scale);
            }

            public Vector3 Offset { get; }

            public Quaternion Rotation { get; }

            public float Scale { get; }

            public Matrix4x4 Linear { get; }

            public static SideMap For(PanelPose? panel, Vec3 referencePosition)
            {
                if (panel is not { } pose)
                {
                    return Identity;
                }

                var reference = new Vector3(referencePosition.X, referencePosition.Y, referencePosition.Z);
                return new SideMap(pose.Position - (pose.Rotation * (reference * pose.Scale)), pose.Rotation, pose.Scale);
            }

            public Vector3 Apply(Vector3 point) => Linear.MultiplyVector(point) + Offset;

            public static Vector3 Solve(SideMap left, SideMap right, float weight, Vector3 target)
            {
                var linear = Matrix4x4.identity;
                for (var i = 0; i < 16; i++)
                {
                    linear[i] = Mathf.Lerp(left.Linear[i], right.Linear[i], weight);
                }

                linear[15] = 1f;
                return linear.inverse.MultiplyVector(target - Vector3.Lerp(left.Offset, right.Offset, weight));
            }
        }

        private readonly struct PanelPose
        {
            public PanelPose(Vector3 position, Quaternion rotation, float scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public Vector3 Position { get; }

            public Quaternion Rotation { get; }

            public float Scale { get; }
        }
    }
}
