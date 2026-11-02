namespace NotCounters.Core.Config
{
    public static class VanillaCaption
    {
        public const string Hidden = "";

        public static bool IsShown(string? labelText) => labelText == null || labelText.Length > 0;

        public static string? ShownText(string? text) => string.IsNullOrWhiteSpace(text) ? null : text!.Trim();
    }
}
