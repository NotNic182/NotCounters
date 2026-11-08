using Zenject;

namespace NotCounters.Services
{
    internal static class ServicesBindings
    {
        public static void InstallApp(DiContainer container)
        {
            container.BindInterfacesTo<ConfigService>().AsSingle();
            container.BindInterfacesTo<UnityHttpClient>().AsSingle();
            container.BindInterfacesAndSelfTo<LeaderboardRequestScheduler>().AsSingle();
            container.BindInterfacesTo<LeaderboardDataService>().AsSingle();

            container.BindInterfacesTo<PlayerAccountService>().AsSingle();
            container.BindInterfacesTo<RemotePersonalBestService>().AsSingle();
            container.BindInterfacesTo<PersonalBestLineService>().AsSingle();
        }

        public static void InstallMenu(DiContainer container)
        {
            container.BindInterfacesTo<LevelSelectionPrefetcher>().AsSingle();
        }
    }
}
