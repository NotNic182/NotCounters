using System;
using NotCounters.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NotCounters.Editor
{
    internal sealed class EditorClickSound
    {
        private const string ButtonSignalName = "UIButtonWasPressed";

        private BasicUIAudioManager? _manager;
        private Signal? _signal;

        public void Play() => Safe.Invoke("EditorClickSound.Play", PlayCore);

        private void PlayCore()
        {
            if (TryResolve())
            {
                _signal!.Raise();
            }
        }

        private bool TryResolve()
        {
            if (_manager != null && _manager.isActiveAndEnabled && _signal != null)
            {
                return true;
            }

            _manager = null;
            _signal = null;
            foreach (var manager in Object.FindObjectsByType<BasicUIAudioManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (manager == null || !manager.isActiveAndEnabled)
                {
                    continue;
                }

                var signal = FindButtonSignal(manager);
                if (signal != null)
                {
                    _manager = manager;
                    _signal = signal;
                    return true;
                }
            }

            Safe.WarnOnce(
                "EditorClickSound.Missing",
                "NotCounters: the game's UI click sound is not available in this scene; pressing HUD elements in the editor stays silent.");
            return false;
        }

        private static Signal? FindButtonSignal(BasicUIAudioManager manager)
        {
            var signals = manager._buttonClickEvents;
            if (signals == null)
            {
                return null;
            }

            Signal? first = null;
            foreach (var signal in signals)
            {
                if (signal == null)
                {
                    continue;
                }

                if (string.Equals(signal.name, ButtonSignalName, StringComparison.Ordinal))
                {
                    return signal;
                }

                first ??= signal;
            }

            return first;
        }
    }
}
