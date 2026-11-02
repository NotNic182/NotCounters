using Zenject;

namespace NotCounters.Gameplay
{
    internal static class GameplayBindings
    {
        public static void InstallPlayer(DiContainer container, bool isEditSession)
        {
            if (isEditSession)
            {
                container.BindInterfacesAndSelfTo<SimulatedStatsSource>().AsSingle();
            }
            else
            {
                container.BindInterfacesAndSelfTo<GameplayStatsHub>().AsSingle();
            }
        }
    }
}
