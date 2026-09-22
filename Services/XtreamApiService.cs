using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Jellyfin.Plugin.MyIPTV.Configuration;
using Jellyfin.Plugin.MyIPTV.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyIPTV.Services
{
    public class XtreamApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<XtreamApiService> _logger;

        public XtreamApiService(IHttpClientFactory httpClientFactory, ILogger<XtreamApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // Pega a configuração atualizada da instância do Plugin
        private static PluginConfiguration Config => Plugin.Instance.Configuration;

        // Exceção pronta pro log, sem senha/link da playlist (a URL costuma aparecer nas mensagens de erro de HTTP).
        public static string SafeError(Exception ex)
            => Naming.Redact(ex.ToString(), Config.Password, Config.PlaylistUrl);

        private bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Config.Host) &&
            !string.IsNullOrWhiteSpace(Config.Username) &&
            !string.IsNullOrWhiteSpace(Config.Password);

        // Host sem barra final e com esquema; usuário/senha codificados pra não quebrar a URL com &, #, + etc.
        private string BaseUrl()
        {
            var host = Config.Host.Trim().TrimEnd('/');
            return host.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? host : "http://" + host;
        }

        private string ApiUrl(string file, string query = "")
            => $"{BaseUrl()}/{file}?username={Uri.EscapeDataString(Config.Username)}&password={Uri.EscapeDataString(Config.Password)}{query}";

        // Erros sobem como exceção (em vez de lista vazia) pra o sync falhar sem sobrescrever arquivos bons com nada.
        private async Task<List<T>> FetchFromApi<T>(string action, string extraParams, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Configuração incompleta. Verifique Host/User/Pass.");
            }

            _logger.LogInformation("[MyIPTV] Consultando API: action={Action}", action);

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            using var response = await client.GetAsync(ApiUrl("player_api.php", $"&action={action}{extraParams}"), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // Login inválido costuma vir como JSON de outro formato, o que estoura JsonException aqui.
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? new List<T>();
        }

        // Métodos públicos usados pelo Channel
        public async Task<List<XtreamCategory>> GetLiveCategories(CancellationToken ct) 
            => await FetchFromApi<XtreamCategory>("get_live_categories", "", ct);

        public async Task<List<XtreamCategory>> GetVodCategories(CancellationToken ct) 
            => await FetchFromApi<XtreamCategory>("get_vod_categories", "", ct);

        public async Task<List<XtreamStream>> GetLiveStreams(string categoryId, CancellationToken ct)
            => await FetchFromApi<XtreamStream>("get_live_streams", $"&category_id={categoryId}", ct);

        public async Task<List<XtreamStream>> GetVodStreams(string categoryId, CancellationToken ct)
            => await FetchFromApi<XtreamStream>("get_vod_streams", $"&category_id={categoryId}", ct);


        public async Task<List<XtreamCategory>> GetSeriesCategories(CancellationToken ct) 
            => await FetchFromApi<XtreamCategory>("get_series_categories", "", ct);

        public async Task<List<XtreamSeries>> GetSeries(string categoryId, CancellationToken ct)
            => await FetchFromApi<XtreamSeries>("get_series", $"&category_id={categoryId}", ct);

        // Versões sem filtro de categoria, usadas pela sincronização completa (M3U/.strm)
        public async Task<List<XtreamStream>> GetAllLiveStreams(CancellationToken ct)
            => await FetchFromApi<XtreamStream>("get_live_streams", "", ct);

        public async Task<List<XtreamStream>> GetAllVodStreams(CancellationToken ct)
            => await FetchFromApi<XtreamStream>("get_vod_streams", "", ct);

        public async Task<List<XtreamSeries>> GetAllSeries(CancellationToken ct)
            => await FetchFromApi<XtreamSeries>("get_series", "", ct);

        // Baixa e faz o parse em streaming do guia XMLTV, retornando nome normalizado -> id do canal no EPG.
        public async Task<Dictionary<string, string>> GetEpgChannelMap(CancellationToken cancellationToken)
        {
            var map = new Dictionary<string, string>();

            if (!IsConfigured)
            {
                return map;
            }

            // EPG é opcional: se falhar, o M3U sai sem tvg-id em vez de o sync inteiro falhar.
            try
            {
                var url = ApiUrl("xmltv.php");

                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(90);

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[MyIPTV] Erro HTTP {StatusCode} ao acessar xmltv.php", response.StatusCode);
                    return map;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore });

                string currentId = null;
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "channel")
                    {
                        currentId = reader.GetAttribute("id");
                    }
                    else if (reader.NodeType == XmlNodeType.Element && reader.Name == "display-name" && currentId != null)
                    {
                        var text = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                        var key = Naming.NormalizeName(text);
                        if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                        {
                            map[key] = currentId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[MyIPTV] Erro ao processar guia XMLTV: {Error}", SafeError(ex));
            }

            return map;
        }

        public async Task<XtreamSeriesInfo> GetSeriesInfo(string seriesId, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("[MyIPTV] Configuração incompleta. Verifique Host/User/Pass.");
                return null;
            }

            var url = ApiUrl("player_api.php", $"&action=get_series_info&series_id={Uri.EscapeDataString(seriesId)}");

            const int maxAttempts = 4;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(15);

                    using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        // 429/503 costumam ser o provedor Xtream limitando conexões simultâneas: vale re-tentar com backoff.
                        var transient = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                            || response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable;
                        if (transient && attempt < maxAttempts)
                        {
                            _logger.LogWarning("[MyIPTV] HTTP {StatusCode} ao acessar get_series_info da série {SeriesId} (tentativa {Attempt}/{Max}), tentando de novo.", response.StatusCode, seriesId, attempt, maxAttempts);
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        _logger.LogError("[MyIPTV] Erro HTTP {StatusCode} ao acessar get_series_info", response.StatusCode);
                        return null;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    return await JsonSerializer.DeserializeAsync<XtreamSeriesInfo>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError("[MyIPTV] Erro ao ler JSON de get_series_info: {Error}", SafeError(jsonEx));
                    return null;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError("[MyIPTV] Erro de conexão ao buscar get_series_info: {Error}", SafeError(ex));
                    return null;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout do HttpClient (não é o cancellationToken externo sendo acionado).
                    _logger.LogWarning("[MyIPTV] Timeout ao buscar get_series_info para a série {SeriesId}.", seriesId);
                    return null;
                }
            }

            return null;
        }

        // Baixa a playlist inteira (dezenas de MB) em streaming e faz o parse linha a linha.
        public async Task<List<MediaEntry>> GetPlaylistEntries(string playlistUrl, CancellationToken cancellationToken)
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            using var response = await client.GetAsync(playlistUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new System.IO.StreamReader(stream);
            var entries = PlaylistParser.Parse(reader, out var skipped);
            if (skipped > 0)
            {
                _logger.LogWarning("[MyIPTV] {Count} episódios da playlist ignorados por não terem 'SxxExx' no nome.", skipped);
            }

            return entries;
        }

        public string BuildStreamUrl(string streamType, string streamId, string extension)
        {
            if (string.IsNullOrWhiteSpace(Config.Host)) return "";

            var typePath = streamType switch
            {
                "live" => "live",
                "series" => "series",
                _ => "movie"
            };

            // Tratamento de extensão
            if (streamType == "live" && string.IsNullOrEmpty(extension)) extension = "ts";
            var ext = string.IsNullOrEmpty(extension) ? "" : $".{extension}";

            return $"{BaseUrl()}/{typePath}/{Uri.EscapeDataString(Config.Username)}/{Uri.EscapeDataString(Config.Password)}/{streamId}{ext}";
        }
    }
}