using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MyIPTV.Services
{
    // Normalização de nomes para casamento com EPG e para nomes de arquivo seguros.
    internal static class Naming
    {
        private static readonly Regex HdBrWords = new Regex(@"\b(hd|fhd|sd|4k|uhd|br|h265|h264|hevc)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonAlnum = new Regex("[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex Colon = new Regex(@"\s*:\s*", RegexOptions.Compiled);
        private static readonly Regex PathSeparators = new Regex(@"[/\\|]", RegexOptions.Compiled);
        private static readonly Regex DroppedChars = new Regex("[<>?*]", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex BracketTags = new Regex(@"\s*\[[^\]]*\]", RegexOptions.Compiled);
        private static readonly Regex TrailingYear = new Regex(@"\s+-\s+((?:19|20)\d{2})\s*$", RegexOptions.Compiled);
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

        // ":" vira " - " (e não "_") pra o Jellyfin ainda achar o título ("Alabama: Presos" -> "Alabama - Presos").
        public static string SafeFileName(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "Sem Nome" : name.Trim();
            name = Colon.Replace(name, " - ");
            name = PathSeparators.Replace(name, "-");
            name = DroppedChars.Replace(name, string.Empty).Replace('"', '\'');
            name = Spaces.Replace(name, " ").Trim();
            if (name.Length > 150)
            {
                name = name.Substring(0, 150).TrimEnd();
            }

            return string.IsNullOrWhiteSpace(name) ? "Sem Nome" : name;
        }

        // Nome de filme/série do jeito que o Jellyfin entende: sem tags "[L]"/"[4K]" e com o ano "(2009)"
        // ("Nome - 2009" -> "Nome (2009)").
        public static string CleanMediaName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var cleaned = BracketTags.Replace(name, string.Empty).Trim();
            return TrailingYear.Replace(cleaned, " ($1)");
        }

        private static readonly Regex PasswordParam = new Regex(@"(password=)[^&\s""]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Tira segredos de textos que vão pro log (mensagens de exceção podem incluir a URL com a senha).
        public static string Redact(string text, params string[] secrets)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            text = PasswordParam.Replace(text, "$1***");
            foreach (var secret in secrets)
            {
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    text = text.Replace(secret, "***", StringComparison.Ordinal)
                               .Replace(Uri.EscapeDataString(secret), "***", StringComparison.Ordinal);
                }
            }

            return text;
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
