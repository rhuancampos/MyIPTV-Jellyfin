using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MyIPTV.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Estas propriedades DEVEM ter o mesmo nome (Case Sensitive) que você usou no JavaScript do HTML
        public string Host { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public PluginConfiguration()
        {
            // Valores padrão
            Host = "";
            Username = "";
            Password = "";
        }
    }
}