using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MyIPTV.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System.Globalization;

namespace Jellyfin.Plugin.MyIPTV
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "Meu IPTV Custom";
        
        // MANTENHA ESTE ID IGUAL AO DO ARQUIVO HTML
        public override Guid Id => Guid.Parse("140d176f-a9c0-4ab7-b29e-d46d7386de87");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = this.Name,
                    // IMPORTANTE: Isso deve bater com o Namespace + Pasta + Nome do arquivo
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                    EnableInMainMenu = true,
                    MenuIcon = "live_tv",
                    MenuSection = "server"
                }
            };
        }
    }
}