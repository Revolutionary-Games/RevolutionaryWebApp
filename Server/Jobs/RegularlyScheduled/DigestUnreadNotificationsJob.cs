namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Services;
using Shared.Models.Enums;

/// <summary>
///   Sends a grouped reminder for notifications still unread after the quiet period.
/// </summary>
[DisableConcurrentExecution(500)]
public class DigestUnreadNotificationsJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IMailQueue mailQueue;
    private readonly IConfiguration configuration;

    public DigestUnreadNotificationsJob(ApplicationDbContext database, IMailQueue mailQueue,
        IConfiguration configuration)
    {
        this.database = database;
        this.mailQueue = mailQueue;
        this.configuration = configuration;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        if (!mailQueue.Configured)
            return;

        var quietHours = Math.Clamp(configuration.GetValue("Notifications:UnreadEmailQuietHours", 4), 1, 168);
        var cutoff = DateTime.UtcNow.AddHours(-quietHours);
        var waiting = await database.UserNotifications
            .Where(n => n.ReadAt == null && n.EmailSentAt == null && n.CreatedAt <= cutoff)
            .OrderBy(n => n.CreatedAt).Take(1000).ToListAsync(cancellationToken);

        foreach (var group in waiting.GroupBy(n => n.UserId))
        {
            var user = await database.Users.Include(u => u.EmailPreferences)
                .FirstOrDefaultAsync(u => u.Id == group.Key, cancellationToken);
            if (user == null)
                continue;

            var notifications = group.ToList();
            if (user.EmailPreferences == null || user.EmailPreferences.Allows(EmailReason.Notifications))
            {
                var baseUrl = configuration["BaseUrl"]?.TrimEnd('/') ?? string.Empty;
                var inboxUrl = $"{baseUrl}/notifications";
                var body = new StringBuilder();
                body.AppendLine($"You have {notifications.Count} unread notifications:");
                body.AppendLine();

                foreach (var notification in notifications.Take(20))
                {
                    body.Append("- ").AppendLine(notification.Message);
                }

                body.AppendLine().Append("View your inbox: ").Append(baseUrl).Append("/notifications");

                var htmlBody = new StringBuilder();
                htmlBody.Append("<h1>Unread notifications</h1>");
                htmlBody.Append("<p>You have ").Append(notifications.Count)
                    .Append(" unread notifications:</p>");
                htmlBody.Append("<ul>");
                foreach (var notification in notifications.Take(20))
                {
                    htmlBody.Append("<li>")
                        .Append(System.Net.WebUtility.HtmlEncode(notification.Message))
                        .Append("</li>");
                }

                htmlBody.Append("</ul><p>View your inbox: <a href=\"")
                    .Append(System.Net.WebUtility.HtmlEncode(inboxUrl))
                    .Append("\">notifications</a>.</p>");

                await mailQueue.SendEmail(new MailRequest(user.Email, "Unread notifications", EmailReason.Notifications)
                {
                    HtmlBody = htmlBody.ToString(),
                    PlainTextBody = body.ToString(),
                    RecipientUserId = user.Id,
                }, cancellationToken);
            }

            // The existing mail sender also checks the same preference at delivery time.
            foreach (var notification in notifications)
                notification.EmailSentAt = DateTime.UtcNow;

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
