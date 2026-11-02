using NotCounters.Contracts;
using NotCounters.Core.Counters;
using Zenject;

namespace NotCounters.Editor
{
    internal sealed class EditPlayHints
    {
        private readonly IConfigService _config;

        public EditPlayHints(IConfigService config, [InjectOptional] GameplayCoreSceneSetupData? sceneSetupData)
        {
            _config = config;
            var settings = sceneSetupData?.playerSpecificSettings;
            PlayerAdvancedHud = settings?.advancedHud ?? true;
            PlayerHidesHud = settings?.noTextsAndHuds ?? false;
        }

        public bool PlayerAdvancedHud { get; }

        public bool PlayerHidesHud { get; }

        public bool ForceAdvancedHud => _config.Config.Global.ForceAdvancedHudElements;

        public bool CanForceAdvancedHud => !PlayerAdvancedHud;

        public bool IsHiddenInPlay(IEditableElement? element) =>
            element != null
            && element.Kind == EditableElementKind.Vanilla
            && AdvancedHudElements.IsHiddenInPlay(element.Id, PlayerAdvancedHud, ForceAdvancedHud);
    }
}
