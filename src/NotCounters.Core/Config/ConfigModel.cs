using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Config
{
    public sealed class HudConfig
    {
        public const int CurrentSchemaVersion = 4;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public GlobalSettings Global { get; set; } = new GlobalSettings();

        public string ActiveProfileId { get; set; } = HudProfile.DefaultProfileId;

        public List<HudProfile> Profiles { get; set; } = new List<HudProfile>();
    }

    public sealed class GlobalSettings
    {
        public bool Enabled { get; set; } = true;

        public string PreviewEnvironment { get; set; } = "DefaultEnvironment";

        public string? PreviewLevelId { get; set; }

        public bool EnableBeatLeader { get; set; } = true;

        public bool EnableScoreSaber { get; set; } = true;

        public string BeatLeaderApiBaseUrl { get; set; } = "https://api.beatleader.xyz";

        public string ScoreSaberApiBaseUrl { get; set; } = "https://scoresaber.com";

        public string? BeatLeaderPlayerId { get; set; }

        public string? ScoreSaberPlayerId { get; set; }

        public bool BeatLeaderNoFailCountsAsZero { get; set; }

        public bool HideCountersWhenHudHidden { get; set; } = true;

        public bool ForceAdvancedHudElements { get; set; }

        public bool UniformHudText { get; set; }

        public bool ShowErrorsInCounters { get; set; } = true;

        public double LeaderboardCacheHours { get; set; } = 24;

        public EditorSettings Editor { get; set; } = new EditorSettings();
    }

    public sealed class EditorSettings
    {
        public bool SnapToGrid { get; set; }

        public float GridSize { get; set; } = 0.05f;

        public float AngleSnapDegrees { get; set; } = 15f;

        public bool FaceHeadOnRelease { get; set; }

        public bool TurnWithController { get; set; }

        public bool InvertThumbstickY { get; set; }

        public float ThumbstickDeadzone { get; set; } = 0.2f;

        public float PushPullSpeed { get; set; } = 2f;

        public float ScaleSpeed { get; set; } = 1f;

        public float PreviewStartFraction { get; set; } = 0.25f;
    }

    public sealed class HudProfile
    {
        public const string DefaultProfileId = "default";

        public string Id { get; set; } = DefaultProfileId;

        public string Name { get; set; } = "Default";

        public HudLayout Layout { get; set; } = new HudLayout();

        public Dictionary<string, HudLayout> EnvironmentOverrides { get; set; } = new Dictionary<string, HudLayout>(StringComparer.Ordinal);
    }

    public sealed class HudLayout
    {
        public Dictionary<string, VanillaElementSettings> Vanilla { get; set; } = new Dictionary<string, VanillaElementSettings>(StringComparer.Ordinal);

        public List<CounterSettings> Counters { get; set; } = new List<CounterSettings>();
    }

    public enum NumberGrouping
    {
        None,
        Space,
        Comma,
        Dot,
    }

    public enum TimeDisplayMode
    {
        Default,
        Elapsed,
        Remaining,
        ElapsedAndTotal,
    }

    public sealed class VanillaElementSettings
    {
        public bool Visible { get; set; } = true;

        public bool Removed { get; set; }

        public Vec3 PositionOffset { get; set; } = Vec3.Zero;

        public Vec3 RotationOffset { get; set; } = Vec3.Zero;

        public float Scale { get; set; } = 1f;

        // off: PositionOffset/RotationOffset are a fixed place from the play area center, same in every environment
        public bool MoveWithHud { get; set; } = true;

        public ColorValue? PrimaryColor { get; set; }

        public ColorValue? SecondaryColor { get; set; }

        public float FontSizeMultiplier { get; set; } = 1f;

        public string? LabelText { get; set; }

        public int? Decimals { get; set; }

        public NumberGrouping NumberGrouping { get; set; } = NumberGrouping.None;

        public TimeDisplayMode TimeMode { get; set; } = TimeDisplayMode.Default;

        public bool HideFullComboLines { get; set; }

        public float OutlineWidth { get; set; }

        public ColorValue OutlineColor { get; set; } = ColorValue.Black;
    }

    public enum LabelPlacement
    {
        Above,
        Below,
        Left,
        Right,
        Inline,
    }

    public enum TextAlign
    {
        Left,
        Center,
        Right,
    }

    public sealed class TextStyle
    {
        public const float DefaultFontSize = 20f;

        public const float DefaultLabelScale = 0.75f;

        public float FontSize { get; set; } = DefaultFontSize;

        public ColorValue ValueColor { get; set; } = ColorValue.White;

        public ColorValue LabelColor { get; set; } = ColorValue.White;

        public bool ShowLabel { get; set; } = true;

        public string? LabelText { get; set; }

        public LabelPlacement LabelPlacement { get; set; } = LabelPlacement.Above;

        public float LabelScale { get; set; } = DefaultLabelScale;

        public TextAlign Alignment { get; set; } = TextAlign.Center;

        public int Decimals { get; set; } = 2;

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        public float OutlineWidth { get; set; }

        public ColorValue OutlineColor { get; set; } = ColorValue.Black;

        public bool ColorByValue { get; set; }
    }

    public enum CounterAttachment
    {
        None,
        LeftPanel,
        RightPanel,
        Hud,
    }

    public sealed class CounterSettings
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Type { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public string? Name { get; set; }

        public CounterAttachment Attachment { get; set; } = CounterAttachment.Hud;

        public Vec3 Position { get; set; } = Vec3.Zero;

        public Vec3 Rotation { get; set; } = Vec3.Zero;

        public float Scale { get; set; } = 1f;

        public TextStyle Style { get; set; } = new TextStyle();

        public Dictionary<string, JToken> Options { get; set; } = new Dictionary<string, JToken>(StringComparer.Ordinal);
    }
}
