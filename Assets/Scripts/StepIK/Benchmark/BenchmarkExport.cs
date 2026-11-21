using System.Globalization;
using System.Text.RegularExpressions;

namespace StepIK.Sandbox
{
    public static class BenchmarkExport
    {
        private static readonly char[] CsvSpecialCharacters = { ',', '"', '\r', '\n' };
        // Unity's pretty JSON can emit non-finite number tokens. Match only full property lines.
        private static readonly Regex NonFiniteJsonNumber = new Regex(
            "^(?<prefix>[ \\t]*\"[^\"]+\"[ \\t]*:[ \\t]*)(?:NaN|-?Infinity)(?<suffix>[ \\t]*,?[ \\t]*\\r?)$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        public static string CsvNumber(double value, string format = "F3") =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? string.Empty
                : value.ToString(format, CultureInfo.InvariantCulture);

        public static string CsvText(string value)
        {
            if (value == null) return string.Empty;
            if (value.IndexOfAny(CsvSpecialCharacters) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string NormalizeMissingJsonNumbers(string prettyJson) =>
            NonFiniteJsonNumber.Replace(prettyJson, "${prefix}null${suffix}");
    }
}
