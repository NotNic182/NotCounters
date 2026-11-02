using Zenject;

namespace NotCounters.Hud
{
    internal static class HudBindings
    {
        internal static void InstallPlayer(DiContainer container, bool isEditSession)
        {
            container.BindInstance(new HudSessionOptions(isEditSession)).AsSingle();

            container.BindInterfacesAndSelfTo<HudAnchorProvider>().AsSingle();
            container.BindInterfacesAndSelfTo<HudLayoutSource>().AsSingle();
            container.BindInterfacesAndSelfTo<EditableElementRegistry>().AsSingle();
            container.Bind<LeaderboardLogos>().AsSingle();
            container.BindInterfacesAndSelfTo<HudStyleFactory>().AsSingle();

            container.Bind<VanillaTextFormatter>().AsSingle();
            container.Bind<AdvancedHudActivator>().AsSingle();
            container.BindInterfacesAndSelfTo<VanillaHudController>().AsSingle();
            container.BindInterfacesTo<VanillaTextPatches>().AsSingle();

            container.BindInterfacesAndSelfTo<CounterManager>().AsSingle();
        }
    }
}
