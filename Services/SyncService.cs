using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyIPTV.Configuration;
using Jellyfin.Plugin.MyIPTV.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyIPTV.Services
{
    // Gera o M3U de Live TV e os .strm de Filmes/Séries a partir da API Xtream.
    public class SyncService
    {
        // Provedores Xtream costumam devolver 503 se muitas get_series_info chegarem juntas; 2 é conservador o bastante
        // pra não estourar o limite deles (o GetSeriesInfo ainda tenta de novo com backoff se acontecer).
        private const int SeriesConcurrency = 2;

        private readonly XtreamApiService _api;
        private readonly ILogger<SyncService> _logger;

        public SyncService(XtreamApiService api, ILogger<SyncService> logger)
        {
            _api = api;
            _logger = logger;
        }

        private PluginConfiguration Config => Plugin.Instance.Configuration;

        public async Task RunFullSyncAsync(IProgress<double> progress, CancellationToken ct)
        {
            await SyncLiveTvAsync(ct).ConfigureAwait(false);
            progress.Report(10);

            await SyncMoviesAsync(ct).ConfigureAwait(false);
            progress.Report(40);

            await SyncSeriesAsync(progress, ct).ConfigureAwait(false);
            progress.Report(100);
        }

        private async Task SyncLiveTvAsync(CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(Config.M3uPath) ? "/config/live-tv.m3u" : Config.M3uPath;

            var categories = await _api.GetLiveCategories(ct).ConfigureAwait(false);
            var catMap = categories.ToDictionary(c => c.Id, c => c.Name);

            var streams = await _api.GetAllLiveStreams(ct).ConfigureAwait(false);
            var epgMap = await _api.GetEpgChannelMap(ct).ConfigureAwait(false);

            var sb = new StringBuilder();
            sb.Append("#EXTM3U\n");
            foreach (var s in streams)
            {
                var rawCategory = catMap.TryGetValue(s.CategoryId ?? string.Empty, out var cn) ? cn : "Outros";
                var group = Naming.CleanGroupTitle(rawCategory);
                var displayName = Naming.StripCategoryPrefix(s.Name, rawCategory);

                // O casamento com o EPG usa sempre o nome completo original, não o encurtado.
                var tvgId = epgMap.TryGetValue(Naming.NormalizeName(s.Name), out var id) ? id : string.Empty;
                var url = _api.BuildStreamUrl("live", s.StreamId, "m3u8");
                sb.Append("#EXTINF:-1 tvg-id=\"").Append(tvgId)
                  .Append("\" tvg-name=\"").Append(Naming.EscapeAttr(displayName))
                  .Append("\" tvg-logo=\"").Append(s.IconUrl)
                  .Append("\" group-title=\"").Append(Naming.EscapeAttr(group))
                  .Append("\",").Append(displayName).Append('\n');
                sb.Append(url).Append('\n');
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(path, sb.ToString(), ct).ConfigureAwait(false);
            _logger.LogInformation("[MyIPTV] M3U de Live TV gerado em {Path}: {Count} canais ({Matched} com EPG).", path, streams.Count, epgMap.Count);
        }

        private async Task SyncMoviesAsync(CancellationToken ct)
        {
            var basePath = string.IsNullOrWhiteSpace(Config.MoviesPath) ? "/data/movies" : Config.MoviesPath;

            var categories = await _api.GetVodCategories(ct).ConfigureAwait(false);
            var catMap = categories.ToDictionary(c => c.Id, c => c.Name);

            var vod = await _api.GetAllVodStreams(ct).ConfigureAwait(false);

            foreach (var v in vod)
            {
                ct.ThrowIfCancellationRequested();
                var cat = Naming.SafeFileName(catMap.TryGetValue(v.CategoryId ?? string.Empty, out var cn) ? cn : "Outros");
                var name = Naming.SafeFileName(v.Name);
                var ext = string.IsNullOrEmpty(v.Extension) ? "mp4" : v.Extension;
                var folder = Path.Combine(basePath, cat, name);
                Directory.CreateDirectory(folder);
                var url = _api.BuildStreamUrl("movie", v.StreamId, ext);
                await File.WriteAllTextAsync(Path.Combine(folder, $"{name}.strm"), url + "\n", ct).ConfigureAwait(false);
            }

            _logger.LogInformation("[MyIPTV] {Count} .strm de filmes gerados em {Path}.", vod.Count, basePath);
        }

        private async Task SyncSeriesAsync(IProgress<double> progress, CancellationToken ct)
        {
            var basePath = string.IsNullOrWhiteSpace(Config.SeriesPath) ? "/data/tvshows" : Config.SeriesPath;

            var categories = await _api.GetSeriesCategories(ct).ConfigureAwait(false);
            var catMap = categories.ToDictionary(c => c.Id, c => c.Name);

            var seriesList = await _api.GetAllSeries(ct).ConfigureAwait(false);

            using var semaphore = new SemaphoreSlim(SeriesConcurrency);
            var done = 0;
            var errors = 0;

            var tasks = seriesList.Select(async s =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await SyncOneSeriesAsync(s, catMap, basePath, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref errors);
                    _logger.LogWarning(ex, "[MyIPTV] Falha ao sincronizar série {Id} ({Name}).", s.SeriesId, s.Name);
                }
                finally
                {
                    semaphore.Release();
                    var count = Interlocked.Increment(ref done);
                    progress.Report(40 + (60.0 * count / seriesList.Count));
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            _logger.LogInformation("[MyIPTV] {Count} séries sincronizadas em {Path} ({Errors} falharam).", seriesList.Count, basePath, errors);
        }

        private async Task SyncOneSeriesAsync(XtreamSeries s, Dictionary<string, string> catMap, string basePath, CancellationToken ct)
        {
            var info = await _api.GetSeriesInfo(s.SeriesId, ct).ConfigureAwait(false);
            if (info == null)
            {
                // GetSeriesInfo já logou o motivo (HTTP/timeout/JSON); propaga pra contar como falha no resumo.
                throw new InvalidOperationException($"get_series_info não retornou dados para a série {s.SeriesId}.");
            }

            if (info.Episodes == null)
            {
                return;
            }

            var cat = Naming.SafeFileName(catMap.TryGetValue(s.CategoryId ?? string.Empty, out var cn) ? cn : "Outros");
            var seriesName = Naming.SafeFileName(s.Name);

            foreach (var (seasonKey, episodes) in info.Episodes)
            {
                if (!int.TryParse(seasonKey, out var seasonNum))
                {
                    seasonNum = 0;
                }

                var seasonFolder = Path.Combine(basePath, cat, seriesName, $"Season {seasonNum:D2}");
                Directory.CreateDirectory(seasonFolder);

                foreach (var ep in episodes)
                {
                    if (!int.TryParse(ep.EpisodeNum, out var epNum))
                    {
                        epNum = 0;
                    }

                    var ext = string.IsNullOrEmpty(ep.Extension) ? "mp4" : ep.Extension;
                    var fileName = Naming.SafeFileName($"{seriesName} - S{seasonNum:D2}E{epNum:D2}");
                    var url = _api.BuildStreamUrl("series", ep.Id, ext);
                    await File.WriteAllTextAsync(Path.Combine(seasonFolder, $"{fileName}.strm"), url + "\n", ct).ConfigureAwait(false);
                }
            }
        }
    }
}
