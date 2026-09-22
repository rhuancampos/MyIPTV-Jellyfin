namespace Jellyfin.Plugin.MyIPTV.Models
{
    public enum MediaKind
    {
        Live,
        Movie,
        Series
    }

    // Item já normalizado, venha da API Xtream ou da playlist M3U. Category é a original do provedor (ex: "Filmes | Drama").
    // Para Series, Name é o nome da série e Season/Episode identificam o episódio.
    public class MediaEntry
    {
        public MediaKind Kind { get; set; }

        public string Name { get; set; }

        public string Category { get; set; }

        public string Url { get; set; }

        public string TvgId { get; set; }

        public string Logo { get; set; }

        public int Season { get; set; }

        public int Episode { get; set; }
    }
}
