namespace ThriveDevCenter.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Discord.Webhook;
using Microsoft.Extensions.Logging;

public class SendDiscordManualWebhookJob
{
    private readonly ILogger<SendDiscordManualWebhookJob> logger;

    public SendDiscordManualWebhookJob(ILogger<SendDiscordManualWebhookJob> logger)
    {
        this.logger = logger;
    }

    public async Task Execute(string hookUrl, string message, CancellationToken cancellationToken)
    {
        using var client = new DiscordWebhookClient(hookUrl);

        await SendDiscordWebhookMessageJob.SendDiscordMessageInChunks(message, client, logger, cancellationToken);
    }
}
