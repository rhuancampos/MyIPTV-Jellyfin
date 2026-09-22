using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MyIPTV.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Estas propriedades DEVEM ter o mesmo nome (Case Sensitive) que você usou no JavaScript do HTML
        public string Host { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        // Opcional: link da playlist (get.php?type=m3u_plus). Se preenchido, o sync usa só ele (sem Host/Usuário/Senha).
        public string PlaylistUrl { get; set; }

        // Caminhos usados pela sincronização (geração de M3U e .strm)
        public string MoviesPath { get; set; }
        public string SeriesPath { get; set; }
        public string M3uPath { get; set; }

        public PluginConfiguration()
        {
            // Valores padrão
            Host = "";
            Username = "";
            Password = "";
            PlaylistUrl = "";
            MoviesPath = "/data/movies";
            SeriesPath = "/data/tvshows";
            M3uPath = "/config/live-tv.m3u";
        }
    }
}