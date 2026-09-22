using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MyIPTV.Models
{
    // Mapeia as categorias (TV ou VOD) vindas da API
    public class XtreamCategory
    {
        [JsonPropertyName("category_id")]
        public string Id { get; set; }

        [JsonPropertyName("category_name")]
        public string Name { get; set; }
    }

    // Mapeia o Stream individual (Canal ou Filme)
    public class XtreamStream
    {
        // A API às vezes manda número, às vezes string. O object ajuda a evitar erros de deserialização, 
        // depois convertemos para string com segurança.
        [JsonPropertyName("stream_id")]
        public object StreamIdRaw { get; set; } 

        public string StreamId => StreamIdRaw?.ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("stream_type")]
        public string StreamType { get; set; } // live, movie

        [JsonPropertyName("stream_icon")]
        public string IconUrl { get; set; }

        [JsonPropertyName("container_extension")]
        public string Extension { get; set; } // mp4, mkv, ts

        // Id do canal no guia (XMLTV), quando o provedor já informa.
        [JsonPropertyName("epg_channel_id")]
        public string EpgChannelId { get; set; }

        [JsonPropertyName("rating")]
        public string Rating { get; set; }
        
        [JsonPropertyName("added")]
        public string Added { get; set; }

        [JsonPropertyName("category_id")]
        public string CategoryId { get; set; }
    }

    public class XtreamSeries
    {
        [JsonPropertyName("series_id")]
        public object SeriesIdRaw { get; set; }
        public string SeriesId => SeriesIdRaw?.ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("cover")]
        public string Cover { get; set; }

        [JsonPropertyName("category_id")]
        public string CategoryId { get; set; }
    }

    // Resposta de get_series_info: episódios agrupados por número da temporada
    public class XtreamSeriesInfo
    {
        [JsonPropertyName("episodes")]
        public Dictionary<string, List<XtreamEpisode>> Episodes { get; set; }
    }

    public class XtreamEpisode
    {
        [JsonPropertyName("id")]
        public object IdRaw { get; set; }
        public string Id => IdRaw?.ToString();

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("container_extension")]
        public string Extension { get; set; }

        [JsonPropertyName("season")]
        public object SeasonRaw { get; set; }
        public string Season => SeasonRaw?.ToString();

        [JsonPropertyName("episode_num")]
        public object EpisodeNumRaw { get; set; }
        public string EpisodeNum => EpisodeNumRaw?.ToString();
    }
}