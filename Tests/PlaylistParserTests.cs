using System.IO;
using System.Linq;
using Jellyfin.Plugin.MyIPTV.Models;
using Jellyfin.Plugin.MyIPTV.Services;
using Xunit;

namespace Jellyfin.Plugin.MyIPTV.Tests
{
    public class PlaylistParserTests
    {
        private const string Sample = @"#EXTM3U
#EXTINF:-1 tvg-id=""Globo.br"" tvg-name=""Globo HD"" tvg-logo=""http://x/g.png"" group-title=""Canais | Globo"",Globo HD
http://h/live/u/p/1.ts
#EXTINF:-1 tvg-id="""" tvg-name=""Capitã Marvel (2019) [4K]"" tvg-logo="""" group-title=""Filmes | 4K UHD"",Capitã Marvel (2019) [4K]
http://h/movie/u/p/2.mp4
#EXTINF:-1 tvg-id="""" tvg-name=""Os Flintstones S01E09"" tvg-logo="""" group-title=""Series | Netflix"",Os Flintstones S01E09
http://h/series/u/p/3.mkv
#EXTINF:-1 group-title=""Series | Netflix"",Sem Numeracao
http://h/series/u/p/4.mp4
";

        [Fact]
        public void Parse_classifies_by_url_and_reads_attributes()
        {
            var e = PlaylistParser.Parse(new StringReader(Sample), out var skipped);

            Assert.Equal(1, skipped);
            Assert.Equal(3, e.Count);

            Assert.Equal(MediaKind.Live, e[0].Kind);
            Assert.Equal("Globo.br", e[0].TvgId);
            Assert.Equal("Canais | Globo", e[0].Category);
            Assert.Equal("http://x/g.png", e[0].Logo);

            Assert.Equal(MediaKind.Movie, e[1].Kind);
            Assert.Equal("Capitã Marvel (2019) [4K]", e[1].Name);

            Assert.Equal(MediaKind.Series, e[2].Kind);
            Assert.Equal("Os Flintstones", e[2].Name);
            Assert.Equal((1, 9), (e[2].Season, e[2].Episode));
            Assert.Equal("http://h/series/u/p/3.mkv", e[2].Url);
        }

        [Fact]
        public void Parse_rejects_non_m3u() =>
            Assert.Throws<InvalidDataException>(() => PlaylistParser.Parse(new StringReader("{\"user_info\":{\"auth\":0}}"), out _));

        [Fact]
        public void Parse_keeps_commas_in_name()
        {
            var e = PlaylistParser.Parse(new StringReader("#EXTM3U\n#EXTINF:-1 group-title=\"A\",Olá, Mundo\nhttp://h/movie/u/p/1.mp4\n"), out _);
            Assert.Equal("Olá, Mundo", e.Single().Name);
        }
    }
}
