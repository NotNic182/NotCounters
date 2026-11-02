namespace NotCounters.Hud
{
    internal sealed class HudSessionOptions
    {
        public HudSessionOptions(bool isEditSession)
        {
            IsEditSession = isEditSession;
        }

        public bool IsEditSession { get; }
    }
}
