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
    // Gera o M3U de Live TV e os .strm de Filmes/Séries, a partir da API Xtream ou de um link de playlist M3U.
    // As duas fontes viram MediaEntry e passam pelos mesmos escritores.
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

        private string MoviesPath => string.IsNullOrWhiteSpace(Config.MoviesPath) ? "/data/movies" : Config.MoviesPath;

        private string SeriesPath => string.IsNullOrWhiteSpace(Config.SeriesPath) ? "/data/tvshows" : Config.SeriesPath;

        private string M3uPath => string.IsNullOrWhiteSpace(Config.M3uPath) ? "/config/live-tv.m3u" : Config.M3uPath;

        public async Task RunFullSyncAsync(IProgress<double> progress, CancellationToken ct)
        {
            // Etapas independentes: uma falha (ex: provedor fora do ar) não impede as outras, mas a tarefa termina como falha.
            var errors = new List<Exception>();

            if (!string.IsNullOrWhiteSpace(Config.PlaylistUrl))
            {
                await RunFromPlaylistAsync(progress, errors, ct).ConfigureAwait(false);
            }
            else
            {
                await RunStageAsync("Live TV", () => SyncLiveTvAsync(ct), errors, ct).ConfigureAwait(false);
                progress.Report(10);

                await RunStageAsync("Filmes", () => SyncMoviesAsync(ct), errors, ct).ConfigureAwait(false);
                progress.Report(40);

                await RunStageAsync("Séries", () => SyncSeriesAsync(progress, ct), errors, ct).ConfigureAwait(false);
            }

            progress.Report(100);

            if (errors.Count > 0)
            {
                throw new AggregateException("Sincronização MyIPTV terminou com falhas.", errors);
            }
        }

        private async Task RunFromPlaylistAsync(IProgress<double> progress, List<Exception> errors, CancellationToken ct)
        {
            List<MediaEntry> entries = null;
            await RunStageAsync("Download da playlist", async () =>
            {
                entries = await _api.GetPlaylistEntries(Config.PlaylistUrl.Trim(), ct).ConfigureAwait(false);
                _logger.LogInformation("[MyIPTV] Playlist baixada: {Count} itens.", entries.Count);
            }, errors, ct).ConfigureAwait(false);

            if (entries == null)
            {
                return;
            }

            progress.Report(20);
            await RunStageAsync("Live TV", () => WriteLiveM3uAsync(entries.Where(e => e.Kind == MediaKind.Live).ToList(), ct), errors, ct).ConfigureAwait(false);
            progress.Report(30);

            await RunStageAsync("Filmes", () => WriteMoviesAsync(entries.Where(e => e.Kind == MediaKind.Movie), ct), errors, ct).ConfigureAwait(false);
            progress.Report(50);

            // Série repetida em outra categoria: fica só na primeira em que apareceu.
            var firstCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var episodes = entries.Where(e => e.Kind == MediaKind.Series)
                .Where(e => firstCategory.TryAdd(SeriesKey(e.Name), e.Category) || firstCategory[SeriesKey(e.Name)] == e.Category)
                .ToList();
            await RunStageAsync("Séries", () => WriteSeriesAsync(episodes, ct), errors, ct).ConfigureAwait(false);
        }

        private async Task RunStageAsync(string name, Func<Task> stage, List<Exception> errors, CancellationToken ct)
        {
            try
            {
                await stage().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError("[MyIPTV] Etapa {Stage} falhou; arquivos existentes foram mantidos: {Error}", name, XtreamApiService.SafeError(ex));
                errors.Add(ex);
            }
        }

        // Só grava se o conteúdo mudou, pra não mexer no mtime de milhares de .strm a cada sync.
        private static async Task WriteIfChangedAsync(string path, string content, CancellationToken ct)
        {
            if (File.Exists(path) && await File.ReadAllTextAsync(path, ct).ConfigureAwait(false) == content)
            {
                return;
            }

            await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
        }

        private static string SeriesKey(string name) => Naming.SafeFileName(Naming.CleanMediaName(name));

        // "Filmes | Drama" -> pasta "Drama".
        private static string CategoryFolder(string category) => Naming.SafeFileName(Naming.CleanGroupTitle(category));

        // ---- Fonte: API Xtream ----

        private async Task SyncLiveTvAsync(CancellationToken ct)
        {
            var categories = await _api.GetLiveCategories(ct).ConfigureAwait(false);
            var catMap = categories.ToDictionary(c => c.Id, c => c.Name);

            var streams = await _api.GetAllLiveStreams(ct).ConfigureAwait(false);

            // O provedor já pode informar o id do guia; o casamento por nome só cobre o que ficou sem.
            var epgMap = streams.Any(s => string.IsNullOrEmpty(s.EpgChannelId))
                ? await _api.GetEpgChannelMap(ct).ConfigureAwait(false)
                : new Dictionary<string, string>();

            var entries = streams.Select(s => new MediaEntry
            {
                Kind = MediaKind.Live,
                Name = s.Name,
                Category = catMap.TryGetValue(s.CategoryId ?? string.Empty, out var cn) ? cn : "Outros",
                TvgId = !string.IsNullOrEmpty(s.EpgChannelId)
                    ? s.EpgChannelId
                    : epgMap.TryGetValue(Naming.NormalizeName(s.Name), out var id) ? id : string.Empty,
                Logo = s.IconUrl,
                Url = _api.BuildStreamUrl("live", s.StreamId, "m3u8")
            }).ToList();

            await WriteLiveM3uAsync(entries, ct).ConfigureAwait(false);
        }

        private async Task SyncMoviesAsync(CancellationToken ct)
        {
            var categories = await _api.GetVodCategories(ct).ConfigureAwait(false);
            var catMap = categories.ToDictionary(c => c.Id, c => c.Name);

            var vod = await _api.GetAllVodStreams(ct).ConfigureAwait(false);
            var entries = vod.Select(v => new MediaEntry
            {
                Kind = MediaKind.Movie,
                Name = v.Name,
                Category = catMap.TryGetValue(v.CategoryId ?? string.Empty, out var cn) ? cn : "Outros",
                Url = _api.BuildStreamUrl("movie", v.StreamId, string.IsNullOrEmpty(v.Extension) ? "mp4" : v.Extension)
            });

            await WriteMoviesAsync(entries, ct).ConfigureAwait(false);
        }

        private async Task SyncSeriesAsync(IProgress<double> progress, CancellationToken ct)
        {
            var categories = await _api.GetSeriesCategories(ct).ConfigureAwait(false);
            var catMap = categories.ToDictionary(c => c.Id, c => c.Name);

            // Série repetida em outra categoria: só a primeira é buscada (poupa chamadas ao provedor).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seriesList = (await _api.GetAllSeries(ct).ConfigureAwait(false)).Where(s => seen.Add(SeriesKey(s.Name))).ToList();

            using var semaphore = new SemaphoreSlim(SeriesConcurrency);
            var done = 0;
            var errors = 0;

            var tasks = seriesList.Select(async s =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await SyncOneSeriesAsync(s, catMap, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref errors);
                    _logger.LogWarning("[MyIPTV] Falha ao sincronizar série {Id} ({Name}): {Error}", s.SeriesId, s.Name, XtreamApiService.SafeError(ex));
                }
                finally
                {
                    semaphore.Release();
                    var count = Interlocked.Increment(ref done);
                    progress.Report(40 + (60.0 * count / seriesList.Count));
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
            _logger.LogInformation("[MyIPTV] {Count} séries sincronizadas em {Path} ({Errors} falharam).", seriesList.Count, SeriesPath, errors);
        }

        private async Task SyncOneSeriesAsync(XtreamSeries s, Dictionary<string, string> catMap, CancellationToken ct)
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

            var category = catMap.TryGetValue(s.CategoryId ?? string.Empty, out var cn) ? cn : "Outros";
            var entries = new List<MediaEntry>();
            foreach (var (seasonKey, episodes) in info.Episodes)
            {
                _ = int.TryParse(seasonKey, out var seasonNum);
                foreach (var ep in episodes)
                {
                    _ = int.TryParse(ep.EpisodeNum, out var epNum);
                    entries.Add(new MediaEntry
                    {
                        Kind = MediaKind.Series,
                        Name = s.Name,
                        Category = category,
                        Season = seasonNum,
                        Episode = epNum,
                        Url = _api.BuildStreamUrl("series", ep.Id, string.IsNullOrEmpty(ep.Extension) ? "mp4" : ep.Extension)
                    });
                }
            }

            await WriteSeriesAsync(entries, ct).ConfigureAwait(false);
        }

        // ---- Escritores (comuns às duas fontes) ----

        private async Task WriteLiveM3uAsync(List<MediaEntry> channels, CancellationToken ct)
        {
            if (channels.Count == 0)
            {
                throw new InvalidOperationException("Provedor devolveu 0 canais; M3U existente mantido.");
            }

            var sb = new StringBuilder();
            sb.Append("#EXTM3U\n");
            foreach (var c in channels)
            {
                var rawCategory = string.IsNullOrWhiteSpace(c.Category) ? "Outros" : c.Category;
                var group = Naming.CleanGroupTitle(rawCategory);
                var displayName = Naming.StripCategoryPrefix(c.Name, rawCategory);

                sb.Append("#EXTINF:-1 tvg-id=\"").Append(c.TvgId)
                  .Append("\" tvg-name=\"").Append(Naming.EscapeAttr(displayName))
                  .Append("\" tvg-logo=\"").Append(c.Logo)
                  .Append("\" group-title=\"").Append(Naming.EscapeAttr(group))
                  .Append("\",").Append(displayName).Append('\n');
                sb.Append(c.Url).Append('\n');
            }

            var path = M3uPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await WriteIfChangedAsync(path, sb.ToString(), ct).ConfigureAwait(false);
            _logger.LogInformation("[MyIPTV] M3U de Live TV gerado em {Path}: {Count} canais ({Matched} com EPG).", path, channels.Count, channels.Count(c => !string.IsNullOrEmpty(c.TvgId)));
        }

        private async Task WriteMoviesAsync(IEnumerable<MediaEntry> movies, CancellationToken ct)
        {
            // Mesmo filme em várias categorias/versões ([L], [4K]): fica só um, senão viraria item duplicado no Jellyfin
            // (ou dois .strm no mesmo caminho). A versão legendada ([L]) só vale se não houver outra; OrderBy é estável.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var written = 0;
            var duplicates = 0;

            foreach (var m in movies.OrderBy(m => m.Name != null && m.Name.Contains("[L]", StringComparison.Ordinal)))
            {
                ct.ThrowIfCancellationRequested();
                var name = Naming.SafeFileName(Naming.CleanMediaName(m.Name));
                if (!seen.Add(name))
                {
                    duplicates++;
                    continue;
                }

                var folder = Path.Combine(MoviesPath, CategoryFolder(m.Category), name);
                Directory.CreateDirectory(folder);
                await WriteIfChangedAsync(Path.Combine(folder, $"{name}.strm"), m.Url + "\n", ct).ConfigureAwait(false);
                written++;
            }

            _logger.LogInformation("[MyIPTV] {Count} .strm de filmes em {Path} ({Dup} repetidos ignorados).", written, MoviesPath, duplicates);
        }

        private async Task WriteSeriesAsync(IEnumerable<MediaEntry> episodes, CancellationToken ct)
        {
            var written = 0;
            foreach (var ep in episodes)
            {
                ct.ThrowIfCancellationRequested();
                var seriesName = Naming.SafeFileName(Naming.CleanMediaName(ep.Name));
                var seasonFolder = Path.Combine(SeriesPath, CategoryFolder(ep.Category), seriesName, $"Season {ep.Season:D2}");
                Directory.CreateDirectory(seasonFolder);

                var fileName = Naming.SafeFileName($"{seriesName} - S{ep.Season:D2}E{ep.Episode:D2}");
                await WriteIfChangedAsync(Path.Combine(seasonFolder, $"{fileName}.strm"), ep.Url + "\n", ct).ConfigureAwait(false);
                written++;
            }

            _logger.LogInformation("[MyIPTV] {Count} .strm de episódios em {Path}.", written, SeriesPath);
        }
    }
}
