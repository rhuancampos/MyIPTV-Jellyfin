using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyIPTV.Services;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo; 
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyIPTV.Channels
{
    public class IptvChannel : IChannel, ISupportsLatestMedia
    {
        private readonly XtreamApiService _apiService;
        private readonly ILogger<IptvChannel> _logger;

        public IptvChannel(IHttpClientFactory httpClientFactory, ILogger<IptvChannel> logger, ILogger<XtreamApiService> serviceLogger)
        {
            _logger = logger;
            _apiService = new XtreamApiService(httpClientFactory, serviceLogger);
        }

        // ALTERADO: O nome que vai aparecer na tela inicial do Jellyfin (em "Canais")
        public string Name => "IPTV"; 
        public string Description => "Plugin para assistir IPTV via Xtream Codes";
        public string DataVersion => "1.0";
        public string HomePageUrl => "";
        public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                MediaTypes = new List<ChannelMediaType> { ChannelMediaType.Video },
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Movie,
                    ChannelMediaContentType.TvExtra,
                    ChannelMediaContentType.Episode // ADICIONADO para suportar séries
                },
                SupportsSortOrderToggle = true,
                DefaultSortFields = new List<ChannelItemSortField> { ChannelItemSortField.Name }
            };
        }

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var items = new List<ChannelItemInfo>();

            if (string.IsNullOrEmpty(query.FolderId))
            {
                items.Add(CreateFolder("cat_live_root", "Canais de TV"));
                items.Add(CreateFolder("cat_series_root", "Séries")); // DESCOMENTADO
                items.Add(CreateFolder("cat_vod_root", "Filmes"));
                return new ChannelItemResult { Items = items };
            }

            // --- Lógica de TV ---
            if (query.FolderId == "cat_live_root")
            {
                var categories = await _apiService.GetLiveCategories(cancellationToken);
                foreach (var cat in categories) items.Add(CreateFolder($"live_cat_{cat.Id}", cat.Name));
            }
            else if (query.FolderId.StartsWith("live_cat_"))
            {
                var realCatId = query.FolderId.Replace("live_cat_", "");
                var streams = await _apiService.GetLiveStreams(realCatId, cancellationToken);
                foreach (var stream in streams) items.Add(CreateVideoItem(stream, "live"));
            }
            // --- Lógica de Filmes ---
            else if (query.FolderId == "cat_vod_root")
            {
                var categories = await _apiService.GetVodCategories(cancellationToken);
                foreach (var cat in categories) items.Add(CreateFolder($"vod_cat_{cat.Id}", cat.Name));
            }
            else if (query.FolderId.StartsWith("vod_cat_"))
            {
                var realCatId = query.FolderId.Replace("vod_cat_", "");
                var streams = await _apiService.GetVodStreams(realCatId, cancellationToken);
                foreach (var stream in streams) items.Add(CreateVideoItem(stream, "movie"));
            }
            // --- Lógica de Séries ---
            else if (query.FolderId == "cat_series_root")
            {
                var categories = await _apiService.GetSeriesCategories(cancellationToken);
                foreach (var cat in categories) items.Add(CreateFolder($"series_cat_{cat.Id}", cat.Name));
            }
            else if (query.FolderId.StartsWith("series_cat_"))
            {
                var realCatId = query.FolderId.Replace("series_cat_", "");
                var seriesList = await _apiService.GetSeries(realCatId, cancellationToken);
                foreach (var series in seriesList)
                {
                    // Séries são tratadas como Pastas (Folders) porque dentro delas existem os episódios.
                    items.Add(new ChannelItemInfo
                    {
                        Id = $"series_id_{series.SeriesId}",
                        Name = series.Name,
                        ImageUrl = series.Cover,
                        Type = ChannelItemType.Folder,
                        FolderType = ChannelFolderType.Container
                    });
                }
            }
            else if (query.FolderId.StartsWith("series_id_"))
            {
                var seriesId = query.FolderId.Replace("series_id_", "");
                var info = await _apiService.GetSeriesInfo(seriesId, cancellationToken);
                if (info?.Episodes != null)
                {
                    foreach (var season in info.Episodes)
                    {
                        foreach (var episode in season.Value)
                        {
                            items.Add(CreateEpisodeItem(episode));
                        }
                    }
                }
            }

            return new ChannelItemResult { Items = items };
        }

        private ChannelItemInfo CreateFolder(string id, string name)
        {
            return new ChannelItemInfo
            {
                Id = id,
                Name = name,
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container
            };
        }

        private ChannelItemInfo CreateVideoItem(Models.XtreamStream stream, string type)
        {
            return new ChannelItemInfo
            {
                Id = stream.StreamId.ToString(),
                Name = stream.Name,
                ImageUrl = stream.IconUrl,
                Type = ChannelItemType.Media,
                ContentType = type == "live" ? ChannelMediaContentType.TvExtra : ChannelMediaContentType.Movie,
                MediaType = ChannelMediaType.Video,
                MediaSources = new List<MediaSourceInfo>
                {
                    new MediaSourceInfo
                    {
                        Path = _apiService.BuildStreamUrl(type, stream.StreamId.ToString(), stream.Extension),
                        Protocol = MediaProtocol.Http
                    }
                }
            };
        }

        private ChannelItemInfo CreateEpisodeItem(Models.XtreamEpisode episode)
        {
            return new ChannelItemInfo
            {
                Id = $"episode_{episode.Id}",
                Name = $"S{episode.Season}:E{episode.EpisodeNum} - {episode.Title}",
                Type = ChannelItemType.Media,
                ContentType = ChannelMediaContentType.Episode,
                MediaType = ChannelMediaType.Video,
                MediaSources = new List<MediaSourceInfo>
                {
                    new MediaSourceInfo
                    {
                        Path = _apiService.BuildStreamUrl("series", episode.Id, episode.Extension),
                        Protocol = MediaProtocol.Http
                    }
                }
            };
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken) => Task.FromResult<DynamicImageResponse>(null);
        public IEnumerable<ImageType> GetSupportedChannelImages() => new List<ImageType>();
        public bool IsEnabledFor(string userId) => true;
        public Task<IEnumerable<ChannelItemInfo>> GetLatestMedia(ChannelLatestMediaSearch request, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<ChannelItemInfo>>(new List<ChannelItemInfo>());
    }
}