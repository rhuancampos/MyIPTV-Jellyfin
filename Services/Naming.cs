using System;
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
        private static readonly char[] PrefixTrimChars = { ' ', '-', ':', '–', '—' };

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

        // Provedores costumam prefixar categorias tipo "Canais | Globo". Fica só a parte depois do último "|".
        public static string CleanGroupTitle(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                return "Outros";
            }

            var idx = categoryName.LastIndexOf('|');
            var cleaned = idx >= 0 && idx < categoryName.Length - 1
                ? categoryName.Substring(idx + 1)
                : categoryName;
            cleaned = cleaned.Trim();
            return string.IsNullOrEmpty(cleaned) ? "Outros" : cleaned;
        }

        // Quando o nome do canal repete o nome da categoria (ex: categoria "A Fazenda 18" e canal
        // "A Fazenda 18 CAM 01 (A)"), fica só a parte que sobra ("CAM 01 (A)").
        public static string StripCategoryPrefix(string channelName, string categoryName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                return channelName;
            }

            var name = channelName.Trim();
            if (string.IsNullOrWhiteSpace(categoryName) ||
                name.Length <= categoryName.Length ||
                !name.StartsWith(categoryName, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            var remainder = name.Substring(categoryName.Length).Trim(PrefixTrimChars).Trim();
            return string.IsNullOrEmpty(remainder) ? name : remainder;
        }

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
