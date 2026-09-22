using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.MyIPTV.Models;

namespace Jellyfin.Plugin.MyIPTV.Services
{
    // Lê a playlist "get.php?type=m3u_plus" do Xtream. Tipo vem do caminho da URL (/live/, /movie/, /series/);
    // episódios vêm achatados como "Nome da Série S01E09".
    internal static class PlaylistParser
    {
        private static readonly Regex Extinf = new Regex(@"^#EXTINF:[^\s,]*(?<attrs>(?:\s+[\w-]+=""[^""]*"")*)\s*,(?<name>.*)$", RegexOptions.Compiled);
        private static readonly Regex Attr = new Regex(@"(?<k>[\w-]+)=""(?<v>[^""]*)""", RegexOptions.Compiled);
        private static readonly Regex KindInUrl = new Regex(@"^https?://[^/]+/(?<k>movie|series)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Episode = new Regex(@"^(?<s>.*?)\s+S(?<season>\d+)E(?<ep>\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Séries sem "SxxExx" no nome não dá pra organizar; ficam de fora e são contadas em skippedSeries.
        public static List<MediaEntry> Parse(TextReader reader, out int skippedSeries)
        {
            skippedSeries = 0;
            var entries = new List<MediaEntry>();

            var first = reader.ReadLine();
            if (first == null || !first.TrimStart('﻿').StartsWith("#EXTM3U", System.StringComparison.Ordinal))
            {
                throw new InvalidDataException("A resposta não é uma playlist M3U (login inválido ou link errado?).");
            }

            Match pending = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith("#EXTINF", System.StringComparison.Ordinal))
                {
                    pending = Extinf.Match(line);
                    continue;
                }

                if (line.Length == 0 || line[0] == '#' || pending == null || !pending.Success)
                {
                    continue;
                }

                var entry = Build(pending, line);
                pending = null;
                if (entry == null)
                {
                    skippedSeries++;
                    continue;
                }

                entries.Add(entry);
            }

            return entries;
        }

        private static MediaEntry Build(Match extinf, string url)
        {
            var attrs = new Dictionary<string, string>();
            foreach (Match m in Attr.Matches(extinf.Groups["attrs"].Value))
            {
                attrs[m.Groups["k"].Value] = m.Groups["v"].Value;
            }

            attrs.TryGetValue("group-title", out var category);
            attrs.TryGetValue("tvg-id", out var tvgId);
            attrs.TryGetValue("tvg-logo", out var logo);

            var entry = new MediaEntry
            {
                Kind = MediaKind.Live,
                Name = extinf.Groups["name"].Value.Trim(),
                Category = category,
                Url = url,
                TvgId = tvgId,
                Logo = logo
            };

            var kind = KindInUrl.Match(url);
            if (!kind.Success)
            {
                return entry;
            }

            if (kind.Groups["k"].Value.ToLowerInvariant() == "movie")
            {
                entry.Kind = MediaKind.Movie;
                return entry;
            }

            var ep = Episode.Match(entry.Name);
            if (!ep.Success)
            {
                return null;
            }

            entry.Kind = MediaKind.Series;
            entry.Name = ep.Groups["s"].Value;
            entry.Season = int.Parse(ep.Groups["season"].Value, System.Globalization.CultureInfo.InvariantCulture);
            entry.Episode = int.Parse(ep.Groups["ep"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return entry;
        }
    }
}
