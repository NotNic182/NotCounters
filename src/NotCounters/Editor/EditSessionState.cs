using System;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Util;

namespace NotCounters.Editor
{
    internal sealed class EditSessionState : IEditSessionState
    {
        private EditSessionRelaunchRequest? _relaunchRequest;

        public bool IsActive { get; private set; }

        public string EnvironmentSerializedName { get; private set; } = string.Empty;

        public HudConfig? ConfigAtStart { get; private set; }

        public event Action? StateChanged;

        public void Begin(string environmentSerializedName, HudConfig configAtStart)
        {
            if (string.IsNullOrEmpty(environmentSerializedName))
            {
                throw new ArgumentException("An environment serialized name is required.", nameof(environmentSerializedName));
            }

            if (configAtStart == null)
            {
                throw new ArgumentNullException(nameof(configAtStart));
            }

            if (IsActive)
            {
                throw new InvalidOperationException("A HUD edit session is already active.");
            }

            _relaunchRequest = null;
            EnvironmentSerializedName = environmentSerializedName;
            ConfigAtStart = configAtStart;
            IsActive = true;
            RaiseStateChanged();
        }

        public void End()
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            ConfigAtStart = null;
            RaiseStateChanged();
        }

        public bool RequestRelaunch(string environmentSerializedName)
        {
            if (!IsActive || string.IsNullOrEmpty(environmentSerializedName) || ConfigAtStart == null)
            {
                return false;
            }

            _relaunchRequest = new EditSessionRelaunchRequest(environmentSerializedName, ConfigAtStart);
            return true;
        }

        public EditSessionRelaunchRequest? TakeRelaunchRequest()
        {
            var request = _relaunchRequest;
            _relaunchRequest = null;
            return request;
        }

        private void RaiseStateChanged()
        {
            var handlers = StateChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                var action = (Action)handler;
                Safe.Invoke("EditSessionState.StateChanged", action);
            }
        }
    }

    internal sealed class EditSessionRelaunchRequest
    {
        public EditSessionRelaunchRequest(string environmentSerializedName, HudConfig configAtStart)
        {
            EnvironmentSerializedName = environmentSerializedName;
            ConfigAtStart = configAtStart;
        }

        public string EnvironmentSerializedName { get; }

        public HudConfig ConfigAtStart { get; }
    }
}
