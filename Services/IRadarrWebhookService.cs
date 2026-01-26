using Apollarr.Models;

namespace Apollarr.Services;

public interface IRadarrWebhookService
{
    Task<WebhookResponse> HandleWebhookAsync(RadarrWebhook webhook, CancellationToken cancellationToken = default);
    Task<MonitorWantedResponse> MonitorWantedAsync(CancellationToken cancellationToken = default);
}
