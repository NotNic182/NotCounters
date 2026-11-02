using UnityEngine;
using VRUIControls;

namespace NotCounters.Editor
{
    internal sealed class PauseMenuFreezer
    {
        private const string ButtonCanvasPath = "MenuWrapper/Canvas";

        private const float RestoreInteractionDelaySeconds = 0.2f;

        private readonly PauseController? _pauseController;
        private readonly PauseMenuManager? _pauseMenuManager;
        private readonly IGamePause? _gamePause;

        private GameObject? _hiddenCanvas;
        private bool _hiddenCanvasWasActive;
        private bool _menuManagerDisabled;
        private bool _menuManagerWasEnabled;

        public PauseMenuFreezer(PauseController? pauseController, PauseMenuManager? pauseMenuManager, IGamePause? gamePause)
        {
            _pauseController = pauseController;
            _pauseMenuManager = pauseMenuManager;
            _gamePause = gamePause;
        }

        public bool CanFreeze => _pauseController != null && _pauseMenuManager != null;

        public bool IsGamePaused
        {
            get
            {
                if (_gamePause != null)
                {
                    return _gamePause.isPaused;
                }

                return _pauseController != null && _pauseController._paused == PauseController.PauseState.Paused;
            }
        }

        public VRPointer? PausePointer => _pauseMenuManager != null ? _pauseMenuManager._vrPointer : null;

        public bool IsPausePointerReady
        {
            get
            {
                var pointer = PausePointer;
                return pointer != null && pointer.isActiveAndEnabled;
            }
        }

        public void RequestPause()
        {
            if (_pauseController != null)
            {
                _pauseController.Pause();
            }
        }

        public bool HidePauseMenu()
        {
            DisableMenuInput();
            return HideButtonCanvas();
        }

        public bool HideButtonCanvas()
        {
            if (_pauseMenuManager == null)
            {
                return false;
            }

            if (_hiddenCanvas == null)
            {
                var canvas = FindButtonCanvas();
                if (canvas == null)
                {
                    return false;
                }

                _hiddenCanvas = canvas;
                _hiddenCanvasWasActive = canvas.activeSelf;
            }

            _hiddenCanvas.SetActive(false);
            return true;
        }

        public void DisableMenuInput()
        {
            if (_pauseMenuManager == null)
            {
                return;
            }

            if (!_menuManagerDisabled)
            {
                _menuManagerWasEnabled = _pauseMenuManager.enabled;
                _menuManagerDisabled = true;
            }

            _pauseMenuManager.enabled = false;
        }

        public void Restore(bool keepMenuManagerDisabled)
        {
            if (_hiddenCanvas != null)
            {
                _hiddenCanvas.SetActive(_hiddenCanvasWasActive);
            }

            _hiddenCanvas = null;

            if (_menuManagerDisabled && !keepMenuManagerDisabled && _pauseMenuManager != null)
            {
                _pauseMenuManager._disabledInteractionRemainingTime =
                    Mathf.Max(_pauseMenuManager._disabledInteractionRemainingTime, RestoreInteractionDelaySeconds);
                _pauseMenuManager.enabled = _menuManagerWasEnabled;
                _menuManagerDisabled = false;
            }
        }

        public bool PressPauseMenuBackButton()
        {
            if (_pauseMenuManager == null)
            {
                return false;
            }

            _pauseMenuManager.MenuButtonPressed();
            return true;
        }

        private GameObject? FindButtonCanvas()
        {
            var menu = _pauseMenuManager!;
            GameObject? candidate = null;

            var container = menu._pauseContainerTransform;
            if (container != null)
            {
                var found = container.Find(ButtonCanvasPath);
                if (found != null)
                {
                    candidate = found.gameObject;
                }
            }

            if (candidate == null && menu._continueButton != null)
            {
                var canvas = menu._continueButton.GetComponentInParent<Canvas>(true);
                if (canvas != null)
                {
                    candidate = canvas.rootCanvas != null ? canvas.rootCanvas.gameObject : canvas.gameObject;
                }
            }

            if (candidate == null)
            {
                return null;
            }

            // never disable a parent of the pointer or the EventSystem goes with it
            var pointer = PausePointer;
            if (pointer != null && pointer.transform.IsChildOf(candidate.transform))
            {
                return null;
            }

            return candidate;
        }
    }
}
