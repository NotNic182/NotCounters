using Zenject;

namespace NotCounters.Editor
{
    internal static class EditorBindings
    {
        public static void InstallApp(DiContainer container)
        {
            container.BindInterfacesAndSelfTo<EditSessionState>().AsSingle();
            container.Bind<EnvironmentCatalog>().AsSingle();
        }

        public static void InstallMenu(DiContainer container)
        {
            container.BindInterfacesAndSelfTo<EditSessionLauncher>().AsSingle();
        }

        public static bool IsEditSessionContainer(DiContainer container) => container.HasBinding<EditSessionMarker>();

        public static void InstallPlayer(DiContainer container, bool isEditSession)
        {
            if (!isEditSession)
            {
                return;
            }

            container.BindInterfacesTo<SongFinishBlocker>().AsSingle();
            container.BindInterfacesAndSelfTo<EditSessionAudioMuter>().AsSingle().NonLazy();
            container.BindInterfacesAndSelfTo<EditSessionController>().AsSingle();
            container.BindInterfacesAndSelfTo<EditSelection>().AsSingle();
            container.Bind<EditPlayHints>().AsSingle();
            container.Bind<EditorClickSound>().AsSingle();
            container.BindInterfacesAndSelfTo<ElementGrabController>().AsSingle();
            container.BindInterfacesTo<EditPreviewValues>().AsSingle();
        }
    }
}
