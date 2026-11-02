using System.Globalization;
using System.Security;

namespace NotCounters.UI.Forms
{
    internal static class BsmlMarkup
    {
        public static string Escape(string? text) => SecurityElement.Escape(text ?? string.Empty) ?? string.Empty;

        public static string Attr(string name, string? value) => " " + name + "='" + Escape(value) + "'";

        public static string Attr(string name, double value) => Attr(name, Number(value));

        public static string Attr(string name, bool value) => Attr(name, value ? "true" : "false");

        public static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
