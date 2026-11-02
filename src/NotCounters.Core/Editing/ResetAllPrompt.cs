namespace NotCounters.Core.Editing
{
    public static class ResetAllPrompt
    {
        public static string ForVanilla(string displayName, bool hasPlacement) =>
            "Reset all settings of the vanilla \"" + displayName + "\"?"
            + (hasPlacement ? " It goes back to where the game puts it." : string.Empty);

        public static string ForCounter(string displayName) =>
            "Reset all settings of the counter \"" + displayName + "\"? It keeps its position.";
    }
}
