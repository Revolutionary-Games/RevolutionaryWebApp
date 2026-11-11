namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models;
using Shared.Models.Enums;

[DisableConcurrentExecution(1000)]
public class NotifyModerationQueueJob : IJob
{
    private readonly ApplicationDbContext database;

    public NotifyModerationQueueJob(ApplicationDbContext database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var oldest = await database.UserReports.AsNoTracking().Where(report => report.ResolvedAt == null)
            .OrderBy(report => report.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (oldest == null || oldest.CreatedAt > now.AddHours(-8))
            return;

        var users = await database.Users.AsNoTracking().Where(user =>
                (user.SuspendedUntil == null || user.SuspendedUntil <= now) && user.Groups.Any(group =>
                    group.Id == GroupType.Admin || group.Id == GroupType.ContentModerator))
            .Select(user => user.Id).ToListAsync(cancellationToken);
        var unread = await database.UserNotifications.AsNoTracking().Where(notification =>
                notification.ReadAt == null && (notification.Type == UserNotificationType.ModerationReminder ||
                    notification.Type == UserNotificationType.ModerationDailyReminder))
            .Select(notification => notification.UserId).Distinct().ToListAsync(cancellationToken);
        if (oldest.CreatedAt <= now.AddHours(-24))
        {
            var dailyCutoff = now.AddHours(-24);
            var recentlyNotified = await database.UserNotifications.AsNoTracking().Where(notification =>
                    notification.Type == UserNotificationType.ModerationDailyReminder &&
                    notification.CreatedAt > dailyCutoff)
                .Select(notification => notification.UserId).Distinct().ToListAsync(cancellationToken);
            foreach (var userId in users.Except(unread).Except(recentlyNotified))
                await AddNotification(userId, UserNotificationType.ModerationDailyReminder, oldest.Id);
        }
        else
        {
            var randomCutoff = now.AddHours(-8);
            if (await database.UserNotifications.AnyAsync(notification =>
                    notification.Type == UserNotificationType.ModerationReminder &&
                    notification.CreatedAt > randomCutoff, cancellationToken))
            {
                return;
            }

            var available = users.Except(unread).ToArray();
            if (available.Length > 0)
            {
                await AddNotification(available[Random.Shared.Next(available.Length)],
                    UserNotificationType.ModerationReminder, oldest.Id);
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task AddNotification(long userId, UserNotificationType type, long reportId)
    {
        await database.UserNotifications.AddAsync(new UserNotification
        {
            UserId = userId, Type = type, Priority = NotificationPriority.High,
            DeduplicationKey = $"moderation:{userId}:{type}:{Guid.NewGuid()}",
            Message = type == UserNotificationType.ModerationDailyReminder ?
                "The moderation queue has reports waiting more than 24 hours. Please review them." :
                "A report has been waiting for moderation for at least 8 hours. Please review it.",
            TargetPath = type == UserNotificationType.ModerationDailyReminder ?
                "/moderation" :
                $"/moderation/{reportId}",
        });
    }
}
