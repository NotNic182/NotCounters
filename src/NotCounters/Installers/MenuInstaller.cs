using NotCounters.Editor;
using NotCounters.Services;
using NotCounters.UI;
using Zenject;

namespace NotCounters.Installers
{
    internal sealed class MenuInstaller : Installer
    {
        public override void InstallBindings()
        {
            ServicesBindings.InstallMenu(Container);
            EditorBindings.InstallMenu(Container);
            UiBindings.InstallMenu(Container);
        }
    }
}
