namespace ThriveDevCenter.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Discord.Webhook;

public class SendDiscordManualWebhookJob
{
    public async Task Execute(string hookUrl, string message, CancellationToken cancellationToken)
    {
        using var client = new DiscordWebhookClient(hookUrl);

        await client.SendMessageAsync(message);
    }
}
