using System;
using BeatSaberMarkupLanguage.MenuButtons;
using NotCounters.Editor;
using NotCounters.Util;
using SiraUtil.Logging;
using Zenject;

namespace NotCounters.UI
{
    internal sealed class MenuButtonRegistrar : IInitializable, IDisposable
    {
        private const string ButtonText = "NotCounters";
        private const string ButtonHint = "Edit the HUD and your counters live, in any environment.";
        private const string WaitingButtonText = "NotCounters (waiting for songs)";
        private const string WaitingButtonHint = "The editor opens by itself as soon as the song list has finished loading. Leaving the main menu cancels it.";

        private readonly MenuButtons _menuButtons;
        private readonly EditSessionLauncher _launcher;
        private readonly SiraLog _log;
        private readonly MenuButton _button;
        private bool _registered;
        private bool _watchingLauncher;

        public MenuButtonRegistrar(MenuButtons menuButtons, EditSessionLauncher launcher, SiraLog log)
        {
            _menuButtons = menuButtons;
            _launcher = launcher;
            _log = log;
            _button = new MenuButton(ButtonText, ButtonHint, OpenEditor);
        }

        public void Initialize()
        {
            try
            {
                _menuButtons.RegisterButton(_button);
                _registered = true;
            }
            catch (Exception ex)
            {
                _log.Error("Could not register the NotCounters menu button: " + ex);
                return;
            }

            _launcher.WaitingChanged += HandleLaunchWaitingChanged;
            _watchingLauncher = true;
            HandleLaunchWaitingChanged();
        }

        public void Dispose()
        {
            if (_watchingLauncher)
            {
                _launcher.WaitingChanged -= HandleLaunchWaitingChanged;
                _watchingLauncher = false;
            }

            if (!_registered)
            {
                return;
            }

            try
            {
                _menuButtons.UnregisterButton(_button);
            }
            catch (Exception ex)
            {
                _log.Warn("Could not unregister the NotCounters menu button: " + ex);
            }

            _registered = false;
        }

        private void OpenEditor() => Safe.Invoke("ui.menuButton.open", () =>
        {
            if (!_launcher.StartFromMenu())
            {
                _log.Warn("The NotCounters live editor did not start (see the messages above).");
            }
        });

        private void HandleLaunchWaitingChanged() => Safe.Invoke("ui.menuButton.waiting", () =>
        {
            var waiting = _launcher.IsWaiting;
            var text = waiting ? WaitingButtonText : ButtonText;
            var hint = waiting ? WaitingButtonHint : ButtonHint;
            if (!string.Equals(_button.Text, text, StringComparison.Ordinal))
            {
                _button.Text = text;
            }

            if (!string.Equals(_button.HoverHint, hint, StringComparison.Ordinal))
            {
                _button.HoverHint = hint;
            }
        });
    }
}
