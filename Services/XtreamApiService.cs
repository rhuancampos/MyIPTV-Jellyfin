using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        private PluginConfiguration Config => Plugin.Instance.Configuration;

        private async Task<List<T>> FetchFromApi<T>(string action, string extraParams, CancellationToken cancellationToken)
        {
            // Validação básica
            if (string.IsNullOrWhiteSpace(Config.Host) || 
                string.IsNullOrWhiteSpace(Config.Username) || 
                string.IsNullOrWhiteSpace(Config.Password))
            {
                _logger.LogWarning("[MyIPTV] Configuração incompleta. Verifique Host/User/Pass.");
                return new List<T>();
            }

            try
            {
                // Limpeza da URL para evitar erros comuns (ex: barra no final)
                var host = Config.Host.TrimEnd('/');
                if (!host.StartsWith("http")) host = "http://" + host;

                var url = $"{host}/player_api.php?username={Config.Username}&password={Config.Password}&action={action}{extraParams}";
                
                // Log para debug (Cuidado: mostra a senha no log se não mascarar, útil apenas em dev)
                _logger.LogInformation($"[MyIPTV] Consultando API: action={action}");

                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                using var response = await client.GetAsync(url, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"[MyIPTV] Erro HTTP {response.StatusCode} ao acessar {action}");
                    return new List<T>();
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                
                // Tenta deserializar. Se a API retornar erro (ex: autenticação falha), ela retorna um JSON diferente 
                // que pode causar exceção aqui, caindo no catch.
                return await JsonSerializer.DeserializeAsync<List<T>>(stream, cancellationToken: cancellationToken) ?? new List<T>();
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "[MyIPTV] Erro ao ler JSON. Provavelmente login inválido ou API fora do ar.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MyIPTV] Erro geral na conexão.");
            }

            return new List<T>();
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

        public async Task<XtreamSeriesInfo> GetSeriesInfo(string seriesId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(Config.Host) ||
                string.IsNullOrWhiteSpace(Config.Username) ||
                string.IsNullOrWhiteSpace(Config.Password))
            {
                _logger.LogWarning("[MyIPTV] Configuração incompleta. Verifique Host/User/Pass.");
                return null;
            }

            try
            {
                var host = Config.Host.TrimEnd('/');
                if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase)) host = "http://" + host;

                var url = $"{host}/player_api.php?username={Config.Username}&password={Config.Password}&action=get_series_info&series_id={seriesId}";

                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[MyIPTV] Erro HTTP {StatusCode} ao acessar get_series_info", response.StatusCode);
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<XtreamSeriesInfo>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "[MyIPTV] Erro ao ler JSON de get_series_info.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[MyIPTV] Erro de conexão ao buscar get_series_info.");
            }

            return null;
        }

        public string BuildStreamUrl(string streamType, string streamId, string extension)
        {
            if (string.IsNullOrWhiteSpace(Config.Host)) return "";

            var host = Config.Host.TrimEnd('/');
            if (!host.StartsWith("http")) host = "http://" + host;

            var typePath = streamType switch
            {
                "live" => "live",
                "series" => "series",
                _ => "movie"
            };

            // Tratamento de extensão
            if (streamType == "live" && string.IsNullOrEmpty(extension)) extension = "ts";
            var ext = string.IsNullOrEmpty(extension) ? "" : $".{extension}";

            return $"{host}/{typePath}/{Config.Username}/{Config.Password}/{streamId}{ext}";
        }
    }
}