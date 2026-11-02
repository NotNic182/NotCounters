using NotCounters.Installers;
using NotCounters.Util;
using IPA;
using SiraUtil.Zenject;
using IPALogger = IPA.Logging.Logger;

namespace NotCounters
{
    [Plugin(RuntimeOptions.SingleStartInit), NoEnableDisable]
    public sealed class Plugin
    {
        internal static IPALogger Log { get; private set; } = null!;

        [Init]
        public Plugin(IPALogger logger, Zenjector zenjector)
        {
            Log = logger;
            Safe.Logger = logger;

            zenjector.UseLogger(logger);
            zenjector.UseMetadataBinder<Plugin>();

            zenjector.Install<AppInstaller>(Location.App);
            zenjector.Install<MenuInstaller>(Location.Menu);

            zenjector.Install<PlayerInstaller>(Location.StandardPlayer | Location.CampaignPlayer);
        }
    }
}
