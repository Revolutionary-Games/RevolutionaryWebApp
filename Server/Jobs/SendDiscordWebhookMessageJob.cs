namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.Webhook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared;

public class SendDiscordWebhookMessageJob
{
    private readonly ILogger<SendDiscordWebhookMessageJob> logger;
    private readonly IConfiguration configuration;

    public SendDiscordWebhookMessageJob(ILogger<SendDiscordWebhookMessageJob> logger, IConfiguration configuration)
    {
        this.logger = logger;
        this.configuration = configuration;
    }

    /// <summary>
    ///   Makes sure sending really long Discord messages work
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <param name="client">Discord client to use</param>
    /// <param name="logger">Where to log problems</param>
    /// <param name="cancellationToken">Cancels the operation</param>
    /// <exception cref="Exception">If sending fails too many times</exception>
    public static async Task SendDiscordMessageInChunks(string message, DiscordWebhookClient client, ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var messageChunk in message.Chunk(AppInfo.MaxDiscordMessageLength))
        {
            bool sent = false;
            Exception? latestError = null;

            for (int i = 0; i < 5; ++i)
            {
                if (i > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10) * i, cancellationToken);
                }

                try
                {
                    await client.SendMessageAsync(new string(messageChunk));
                    sent = true;
                    break;
                }
                catch (Exception e)
                {
                    latestError = e;
                    logger.LogWarning(e, "Sending Discord webhook message part failed");
                }
            }

            if (!sent)
                throw new Exception("Sending a Discord message webhook part failed too many times", latestError);
        }
    }

    public async Task Execute(string hookName, string message, CancellationToken cancellationToken)
    {
        var key = LoadKeyForHook(hookName);

        if (key == null)
            return;

        using var client = new DiscordWebhookClient(key);

        await SendDiscordMessageInChunks(message, client, logger, cancellationToken);
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
