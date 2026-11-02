using NotCounters.Contracts;
using NotCounters.Editor;
using NotCounters.Gameplay;
using NotCounters.Hud;
using NotCounters.UI;
using NotCounters.Util;
using Zenject;

namespace NotCounters.Installers
{
    internal sealed class PlayerInstaller : Installer
    {
        private readonly IEditSessionState? _editSession;
        private readonly IConfigService? _config;

        public PlayerInstaller([InjectOptional] IEditSessionState? editSession, [InjectOptional] IConfigService? config)
        {
            _editSession = editSession;
            _config = config;
        }

        public override void InstallBindings()
        {
            Safe.ResetLogOnce();

            if (_config == null)
            {
                Safe.WarnOnce("PlayerInstaller.NoConfig", "NotCounters: the config service is unavailable; nothing is installed in gameplay.");
                return;
            }

            bool isEditSession = IsEditSession();
            if (!isEditSession && !IsModEnabled(_config))
            {
                return;
            }

            GameplayBindings.InstallPlayer(Container, isEditSession);
            HudBindings.InstallPlayer(Container, isEditSession);
            EditorBindings.InstallPlayer(Container, isEditSession);
            UiBindings.InstallPlayer(Container, isEditSession);
        }

        private bool IsEditSession()
        {
            if (_editSession == null || !_editSession.IsActive)
            {
                return false;
            }

            if (EditorBindings.IsEditSessionContainer(Container))
            {
                return true;
            }

            Safe.WarnOnce(
                "PlayerInstaller.MarkerMissing",
                "NotCounters: an edit session is active but this gameplay scene was not started by the live editor; installing normal gameplay.");
            return false;
        }

        private static bool IsModEnabled(IConfigService config) =>
            Safe.Invoke("PlayerInstaller.ReadEnabled", () => config.Config.Global.Enabled, fallback: false);
    }
}
