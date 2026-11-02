using NotCounters.Editor;
using NotCounters.Services;
using Zenject;

namespace NotCounters.Installers
{
    internal sealed class AppInstaller : Installer
    {
        public override void InstallBindings()
        {
            ServicesBindings.InstallApp(Container);
            EditorBindings.InstallApp(Container);
        }
    }
}
