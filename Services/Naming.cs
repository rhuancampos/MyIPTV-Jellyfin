using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MyIPTV.Services
{
    // Normalização de nomes para casamento com EPG e para nomes de arquivo seguros.
    internal static class Naming
    {
        private static readonly Regex HdBrWords = new Regex(@"\b(hd|fhd|sd|4k|uhd|br)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonAlnum = new Regex("[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex InvalidFileChars = new Regex("[<>:\"/\\\\|?*]", RegexOptions.Compiled);

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var stripped = StripDiacritics(name).ToLowerInvariant();
            stripped = HdBrWords.Replace(stripped, string.Empty);
            stripped = NonAlnum.Replace(stripped, string.Empty);
            return stripped;
        }

        public static string SafeFileName(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "Sem Nome" : name.Trim();
            name = InvalidFileChars.Replace(name, "_");
            if (name.Length > 150)
            {
                name = name.Substring(0, 150);
            }

            return string.IsNullOrWhiteSpace(name) ? "Sem Nome" : name;
        }

        public static string EscapeAttr(string value) => (value ?? string.Empty).Replace("\"", "'");

        private static string StripDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
