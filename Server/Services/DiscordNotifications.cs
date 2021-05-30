namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Threading.Tasks;
    using Discord.Webhook;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;

    /// <summary>
    ///   Sends discord messages through webhooks to notify about various things
    /// </summary>
    public class DiscordNotifications : IDisposable
    {
        private readonly ILogger<DiscordNotifications> logger;
        private readonly string botdNotification;

        private DiscordWebhookClient botdClient;

        public DiscordNotifications(ILogger<DiscordNotifications> logger, IConfiguration configuration)
        {
            this.logger = logger;
            botdNotification = configuration["Discord:BOTDNotification"];
        }

        public async Task NotifyAboutNewBOTD(DevBuild build, string setBy)
        {
            if (string.IsNullOrEmpty(botdNotification))
            {
                logger.LogWarning("BOTD webhook not configured, skipping sending notification");
                return;
            }

            botdClient ??= new DiscordWebhookClient(botdNotification);

            await botdClient.SendMessageAsync($"New build of the day (BOTD) set by {setBy}:\n{build.Description}");
        }

        public void Dispose()
        {
            botdClient?.Dispose();
        }
    }
}
