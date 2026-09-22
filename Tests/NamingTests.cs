using Jellyfin.Plugin.MyIPTV.Services;
using Xunit;

namespace Jellyfin.Plugin.MyIPTV.Tests
{
    public class NamingTests
    {
        [Theory]
        [InlineData("Globo HD BR", "globo")]
        [InlineData("Ação 4K", "acao")]
        [InlineData("ESPN H265", "espn")]
        [InlineData(null, "")]
        public void NormalizeName(string input, string expected) => Assert.Equal(expected, Naming.NormalizeName(input));

        [Theory]
        [InlineData("a/b:c?", "a-b - c")]
        [InlineData("Alabama: Presos do Sistema (2025)", "Alabama - Presos do Sistema (2025)")]
        [InlineData("  ", "Sem Nome")]
        public void SafeFileName(string input, string expected) => Assert.Equal(expected, Naming.SafeFileName(input));

        [Fact]
        public void SafeFileName_truncates() => Assert.Equal(150, Naming.SafeFileName(new string('x', 300)).Length);

        [Theory]
        [InlineData("Canais | Globo", "Globo")]
        [InlineData("Globo", "Globo")]
        [InlineData("Canais |", "Canais |")]
        [InlineData("", "Outros")]
        public void CleanGroupTitle(string input, string expected) => Assert.Equal(expected, Naming.CleanGroupTitle(input));

        [Theory]
        [InlineData("A Fazenda 18 CAM 01 (A)", "A Fazenda 18", "CAM 01 (A)")]
        [InlineData("A Fazenda 18", "A Fazenda 18", "A Fazenda 18")]
        [InlineData("Globo", "Esportes", "Globo")]
        public void StripCategoryPrefix(string channel, string category, string expected)
            => Assert.Equal(expected, Naming.StripCategoryPrefix(channel, category));

        [Theory]
        [InlineData("Capitã Marvel (2019) [4K]", "Capitã Marvel (2019)")]
        [InlineData("Ela Disse Talvez (2025) [L]", "Ela Disse Talvez (2025)")]
        [InlineData("16 Quadras - 2006", "16 Quadras (2006)")]
        [InlineData("Filme - 2009 [L]", "Filme (2009)")]
        [InlineData("Os Sapos (2025)", "Os Sapos (2025)")]
        [InlineData("2001 - Uma Odisseia", "2001 - Uma Odisseia")]
        public void CleanMediaName(string input, string expected) => Assert.Equal(expected, Naming.CleanMediaName(input));

        [Fact]
        public void Redact_hides_password_in_url_and_secrets()
        {
            var text = "Get http://h/get.php?username=u&password=s3cr&#t&type=m3u failed; path /live/u/s3cr&#t/1.ts; enc s3cr%26%23t";
            var result = Naming.Redact(text, "s3cr&#t");
            Assert.DoesNotContain("s3cr", result);
            Assert.Contains("username=u", result);
        }
    }
}
