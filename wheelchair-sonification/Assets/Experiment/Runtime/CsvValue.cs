using System.Globalization;

namespace WheelchairSonification.Runtime
{
    // Shared representation for research trials, device checks and auxiliary observations.
    internal static class CsvValue
    {
        internal static string Number(double? value) =>
            value?.ToString("R", CultureInfo.InvariantCulture) ?? "";

        internal static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? ""
            : value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
