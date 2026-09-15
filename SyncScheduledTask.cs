using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyIPTV.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyIPTV
{
    // Tarefa agendável do Jellyfin que gera/atualiza o M3U de Live TV e os .strm de Filmes/Séries.
    public class SyncScheduledTask : IScheduledTask
    {
        private readonly SyncService _syncService;
        private readonly ILogger<SyncScheduledTask> _logger;

        public SyncScheduledTask(
            IHttpClientFactory httpClientFactory,
            ILogger<SyncScheduledTask> logger,
            ILogger<XtreamApiService> apiLogger,
            ILogger<SyncService> syncLogger)
        {
            _logger = logger;
            var api = new XtreamApiService(httpClientFactory, apiLogger);
            _syncService = new SyncService(api, syncLogger);
        }

        public string Name => "Sincronizar MyIPTV";

        public string Key => "MyIptvSyncTask";

        public string Description => "Gera o M3U de Live TV e os arquivos .strm de Filmes/Séries a partir do provedor Xtream configurado.";

        public string Category => "Meu IPTV Custom";

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("[MyIPTV] Iniciando sincronização.");
            await _syncService.RunFullSyncAsync(progress, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("[MyIPTV] Sincronização concluída.");
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
            };
        }
    }
}
