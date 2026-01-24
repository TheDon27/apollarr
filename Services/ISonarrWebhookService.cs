using Apollarr.Models;

namespace Apollarr.Services;

public interface ISonarrWebhookService
{
    Task<WebhookResponse> HandleWebhookAsync(SonarrWebhook webhook, CancellationToken cancellationToken = default);
    Task<MonitorWantedResponse> MonitorWantedAsync(CancellationToken cancellationToken = default);
    Task<RebuildSeriesResponse> RebuildSeriesAsync(CancellationToken cancellationToken = default);
}
