namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Models;
    using Shared.Models.Enums;
    using Utilities;

    public class CheckOverallBuildStatusJob
    {
        private readonly ILogger<CheckOverallBuildStatusJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly DiscordNotifications discordNotifications;
        private readonly IMailQueue mailQueue;
        private readonly BuildReportType discordNotice;
        private readonly Uri baseUrl;
        private readonly bool sendEmails;
        private readonly HashSet<string> alreadySentEmails = new();

        public CheckOverallBuildStatusJob(ILogger<CheckOverallBuildStatusJob> logger, IConfiguration configuration,
            NotificationsEnabledDb database, DiscordNotifications discordNotifications, IMailQueue mailQueue)
        {
            this.logger = logger;
            this.database = database;
            this.discordNotifications = discordNotifications;
            this.mailQueue = mailQueue;

            discordNotice = Enum.Parse<BuildReportType>(configuration["CI:StatusReporting:Discord"]);
            sendEmails = Convert.ToBoolean(configuration["CI:StatusReporting:Email"]);
            baseUrl = configuration.GetBaseUrl();
        }

        public async Task Execute(long ciProjectId, long ciBuildId, CancellationToken cancellationToken)
        {
            // Project is loaded here to use the name in build status reporting
            var build = await database.CiBuilds.Include(b => b.CiJobs).Include(b => b.CiProject)
                .FirstOrDefaultAsync(b => b.CiProjectId == ciProjectId && b.CiBuildId == ciBuildId,
                    cancellationToken);

            if (build == null)
            {
                logger.LogError("Failed to get CI build to check overall status on");
                return;
            }

            // If the status has already been set, ignore
            switch (build.Status)
            {
                case BuildStatus.Succeeded:
                case BuildStatus.Failed:
                    return;
            }

            BuildStatus shouldBeStatus;
            int failedBuilds = 0;

            bool running = false;

            foreach (var job in build.CiJobs)
            {
                if (job.State != CIJobState.Finished)
                {
                    running = true;
                }
                else if (!job.Succeeded)
                {
                    ++failedBuilds;
                }
            }

            if (failedBuilds > 0)
            {
                shouldBeStatus = running ? BuildStatus.GoingToFail : BuildStatus.Failed;
            }
            else if (running)
            {
                shouldBeStatus = BuildStatus.Running;
            }
            else
            {
                shouldBeStatus = BuildStatus.Succeeded;
            }

            if (build.Status == shouldBeStatus)
                return;

            build.Status = shouldBeStatus;
            await database.SaveChangesAsync(cancellationToken);

            // Don't send notifications yet if we only know that the build is going to fail, but not all jobs
            // are complete yet
            if (build.Status == BuildStatus.Running || build.Status == BuildStatus.GoingToFail)
                return;

            // Discord notice
            bool reportDiscord = false;

            switch (discordNotice)
            {
                case BuildReportType.Never:
                    break;
                case BuildReportType.Always:
                    reportDiscord = true;
                    break;
                case BuildReportType.OnFailure:
                    reportDiscord = build.Status == BuildStatus.Failed;
                    break;
                case BuildReportType.OnSuccess:
                    reportDiscord = build.Status == BuildStatus.Succeeded;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var checkLink = new Uri(baseUrl, $"/ci/{build.CiProjectId}/build/{build.CiBuildId}").ToString();

            if (reportDiscord)
            {
                discordNotifications.NotifyAboutBuild(build, checkLink);
            }

            // Send emails on failure
            if (sendEmails && build.Status == BuildStatus.Failed && mailQueue.Configured)
            {
                try
                {
                    foreach (var commit in build.ParsedCommits)
                    {
                        if (!string.IsNullOrEmpty(commit.Author?.Email))
                            await QueueEmailNotification(build, commit.Author.Email, checkLink);

                        if (!string.IsNullOrEmpty(commit.Committer?.Email))
                            await QueueEmailNotification(build, commit.Committer.Email, checkLink);
                    }
                }
                catch (JsonException e)
                {
                    logger.LogError(
                        "Failed to send email notification because ParsedCommits failed to be accessed: {@E}", e);
                }
            }
        }

        private Task QueueEmailNotification(CiBuild build, string email, string checkLink)
        {
            // Skip duplicate emails and likely no reply addresses
            if (!alreadySentEmails.Add(email) || EmailHelpers.IsNoReplyAddress(email))
                return Task.CompletedTask;

            logger.LogInformation("Sending build failure / status ({CiProjectId}-{CiBuildId}) email to: {Email}",
                build.CiProjectId, build.CiBuildId, email);

            var status = "Failed";

            if (build.Status == BuildStatus.Succeeded)
            {
                status = "Succeeded";
            }
            else if (build.Status == BuildStatus.Running)
            {
                // TODO: a bit bad wording but these types of emails should never be sent
                status = "Been Running";
            }

            const string receiveReason =
                "You are receiving this email because your email was associated either with the committer or pusher " +
                "of the commit(s) causing this build. Please email webmaster at the sender domain if you receive " +
                "this email in error.";

            var statusMessage = $"{build.CiProject.Name} build #{build.CiBuildId} has {status.ToLowerInvariant()}." +
                $" This build was triggered for commit {build.CommitHash} from ref {build.RemoteRef}.";

            var htmlBuilder = new StringBuilder(200);
            var plainBuilder = new StringBuilder(200);

            // Intro
            htmlBuilder.Append("<h1>");
            htmlBuilder.Append("Build ");
            htmlBuilder.Append(status);
            htmlBuilder.Append("</h1>");

            htmlBuilder.Append("<p>");
            htmlBuilder.Append(statusMessage);
            htmlBuilder.Append("</p>");

            plainBuilder.Append("Build ");
            plainBuilder.Append(status);
            plainBuilder.Append('\n');
            plainBuilder.Append(statusMessage);

            // Job statuses
            htmlBuilder.Append("<h2>");
            htmlBuilder.Append("Jobs");
            htmlBuilder.Append("</h2>");

            plainBuilder.Append('\n');
            plainBuilder.Append("Jobs");

            htmlBuilder.Append("<ul>");
            foreach (var job in build.CiJobs)
            {
                htmlBuilder.Append("<li>");

                htmlBuilder.Append("<span>");
                htmlBuilder.Append(job.JobName);
                htmlBuilder.Append(" </span>");

                if (job.Succeeded)
                {
                    htmlBuilder.Append("Succeeded");
                }
                else
                {
                    htmlBuilder.Append("<strong>");
                    htmlBuilder.Append("Failed");
                    htmlBuilder.Append("</strong>");
                }

                htmlBuilder.Append("</li>");

                plainBuilder.Append('\n');
                plainBuilder.Append(job.JobName);
                plainBuilder.Append(": ");
                plainBuilder.Append(job.Succeeded ? "Succeeded" : "FAILED");
            }

            htmlBuilder.Append("</ul>");

            // Link to status
            htmlBuilder.Append("<br />");
            htmlBuilder.Append("<p>");
            htmlBuilder.Append("You can view the build output and status online ");
            htmlBuilder.Append("<a href=\"");
            htmlBuilder.Append(checkLink);
            htmlBuilder.Append("\">");
            htmlBuilder.Append("here");
            htmlBuilder.Append("</a>.");
            htmlBuilder.Append("</p>");

            plainBuilder.Append('\n');
            plainBuilder.Append('\n');
            plainBuilder.Append("You can view the build output and status online here: ");
            plainBuilder.Append(checkLink);
            plainBuilder.Append('\n');

            // Footer
            htmlBuilder.Append("<p style=\"color: #444; font-size: 0.9em;\">");
            htmlBuilder.Append(receiveReason);
            htmlBuilder.Append("</p>");

            plainBuilder.Append('\n');
            plainBuilder.Append(receiveReason);

            return mailQueue.SendEmail(new MailRequest
            {
                Recipient = email,
                Subject = $"{build.CiProject.Name} Build #{build.CiBuildId} Has {status}",
                HtmlBody = htmlBuilder.ToString(),
                PlainTextBody = plainBuilder.ToString(),
            }, CancellationToken.None);
        }
    }
}
