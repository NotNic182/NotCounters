using System;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Util;
using Zenject;

namespace NotCounters.Hud
{
    internal sealed class HudLayoutSource : IInitializable, IDisposable
    {
        private readonly IConfigService _config;
        private readonly IHudAnchorProvider _anchors;
        private readonly IEditSessionState? _editSession;

        public HudLayoutSource(
            IConfigService config,
            IHudAnchorProvider anchors,
            HudSessionOptions options,
            [InjectOptional] IEditSessionState? editSession)
        {
            _config = config;
            _anchors = anchors;
            _editSession = editSession;
            IsEditSession = options.IsEditSession;
        }

        public event Action? Changed;

        public bool IsEditSession { get; }

        public GlobalSettings Global => _config.Config.Global;

        public bool IsEnabled => IsEditSession || Global.Enabled;

        public string Environment =>
            IsEditSession && _editSession is { IsActive: true } && !string.IsNullOrEmpty(_editSession.EnvironmentSerializedName)
                ? _editSession.EnvironmentSerializedName
                : _anchors.EnvironmentSerializedName;

        public HudLayout GetLayout() => _config.GetEffectiveLayout(Environment);

        public void Initialize()
        {
            _config.Changed += HandleConfigChanged;
            if (_editSession != null)
            {
                _editSession.StateChanged += HandleConfigChanged;
            }
        }

        public void Dispose()
        {
            _config.Changed -= HandleConfigChanged;
            if (_editSession != null)
            {
                _editSession.StateChanged -= HandleConfigChanged;
            }
        }

        private void HandleConfigChanged() => RaiseChanged();

        private void RaiseChanged()
        {
            var handlers = Changed;
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                Safe.Invoke("hud:layout-changed:" + handler.Method.DeclaringType?.Name, () => ((Action)handler)());
            }
        }
    }
}
