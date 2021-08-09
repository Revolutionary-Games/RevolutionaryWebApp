namespace ThriveDevCenter.Server.Services
{
    using System.Threading;
    using Hangfire;
    using Jobs;
    using Models;

    /// <summary>
    ///   Sends discord messages through webhooks to notify about various things. Uses a background job to allow
    ///   retries
    /// </summary>
    public class DiscordNotifications
    {
        private readonly IBackgroundJobClient jobClient;

        public DiscordNotifications(IBackgroundJobClient jobClient)
        {
            this.jobClient = jobClient;
        }

        public void NotifyAboutNewBOTD(DevBuild build, string setBy)
        {
            jobClient.Enqueue<SendDiscordWebhookMessageJob>(x => x.Execute("BOTDNotification",
                $"New build of the day (BOTD) set by {setBy}:\n{build.Description}", CancellationToken.None));
        }
    }
}
