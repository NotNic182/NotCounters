using NotCounters.UI.Panels;
using Zenject;

namespace NotCounters.UI
{
    internal static class UiBindings
    {
        public static void InstallMenu(DiContainer container)
        {
            container.BindInterfacesTo<MenuButtonRegistrar>().AsSingle();
        }

        public static void InstallPlayer(DiContainer container, bool isEditSession)
        {
            if (!isEditSession)
            {
                return;
            }

            container.Bind<ElementsPanel>().AsSingle();
            container.Bind<SessionPanel>().AsSingle();
            container.BindInterfacesTo<EditorPanels>().AsSingle();
        }
    }
}
