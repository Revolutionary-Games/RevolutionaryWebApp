namespace ThriveDevCenter.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Discord.Webhook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class SendDiscordWebhookMessageJob
{
    private readonly ILogger<SendDiscordWebhookMessageJob> logger;
    private readonly IConfiguration configuration;

    public SendDiscordWebhookMessageJob(ILogger<SendDiscordWebhookMessageJob> logger, IConfiguration configuration)
    {
        this.logger = logger;
        this.configuration = configuration;
    }

    public async Task Execute(string hookName, string message, CancellationToken cancellationToken)
    {
        var key = LoadKeyForHook(hookName);

        if (key == null)
            return;

        using var client = new DiscordWebhookClient(key);

        await client.SendMessageAsync(message);
    }

    private string? LoadKeyForHook(string hook)
    {
        var key = configuration[$"Discord:{hook}"];

        if (string.IsNullOrEmpty(key))
        {
            logger.LogWarning("Discord webhook ({Hook}) not configured, skipping sending message", hook);
            return null;
        }

        return key;
    }
}