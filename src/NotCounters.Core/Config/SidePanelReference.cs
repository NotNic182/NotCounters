namespace NotCounters.Core.Config
{
    public static class SidePanelReference
    {
        public const float ElementScale = 0.01f;

        public static readonly Vec3 LeftPanelElementPosition = new Vec3(-3.2f, 1.83f, 7f);

        public static readonly Vec3 RightPanelElementPosition = new Vec3(3.2f, 1.7f, 7f);

        public static Vec3 ElementPosition(CounterAttachment attachment)
        {
            switch (attachment)
            {
                case CounterAttachment.LeftPanel: return LeftPanelElementPosition;
                case CounterAttachment.RightPanel: return RightPanelElementPosition;
                default: return Vec3.Zero;
            }
        }
    }
}
