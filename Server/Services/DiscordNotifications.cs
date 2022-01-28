namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using Hangfire;
    using Jobs;
    using Models;
    using Shared.Models;

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

        public void NotifyAboutBuild(CiBuild build, string statusUrl)
        {
            var message = new StringBuilder(100);

            message.Append(build.CiProject?.Name ?? "unknown project");
            message.Append(" build nro ");
            message.Append(build.CiBuildId);

            message.Append(" (for: ");
            message.Append(build.RemoteRef);
            message.Append(')');

            switch (build.Status)
            {
                case BuildStatus.Running:
                    message.Append(" is still running");
                    break;
                case BuildStatus.Succeeded:
                    message.Append(" has succeeded.");
                    break;
                case BuildStatus.Failed:
                    message.Append(" has failed");
                    break;
                case BuildStatus.GoingToFail:
                    message.Append(" is going to fail");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (build.Status != BuildStatus.Succeeded)
            {
                message.Append(". with ");
                message.Append(build.CiJobs.Count(j => j.Succeeded));
                message.Append('/');
                message.Append(build.CiJobs.Count);

                message.Append(" successful jobs.");
            }

            message.Append(' ');
            message.Append(statusUrl);

            jobClient.Enqueue<SendDiscordWebhookMessageJob>(x => x.Execute("CIBuildNotification",
                message.ToString(), CancellationToken.None));
        }

        public void NotifyAboutNewCrashReport(CrashReport report, Uri baseUrl)
        {
            var infoUrl = CrashReportUrl(baseUrl, report.Id);

            jobClient.Enqueue<SendDiscordWebhookMessageJob>(x => x.Execute("CrashReportNotification",
                $"New crash report (id: {report.Id}) created for {report.StoreOrVersion} on {report.Platform} {infoUrl}",
                CancellationToken.None));
        }

        public void NotifyCrashReportStateChanged(CrashReport report, Uri baseUrl)
        {
            var infoUrl = CrashReportUrl(baseUrl, report.Id);

            jobClient.Enqueue<SendDiscordWebhookMessageJob>(x => x.Execute("CrashReportNotification",
                $"Crash report {report.Id} is now in state {report.State} {infoUrl}",
                CancellationToken.None));
        }

        private static Uri CrashReportUrl(Uri baseUrl, long id)
        {
            return new Uri(baseUrl, $"/reports/{id}");
        }
    }
}
