namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Shared.Models;

/// <summary>
///   Combines noisy subscription notices while retaining each event's deduplication record.
/// </summary>
public static class UserNotificationBatching
{
    private static readonly UserNotificationType[] CombinedTypes =
    [
        UserNotificationType.TranslationUpdated,
        UserNotificationType.TranslationKeyAdded,
        UserNotificationType.TranslationSourceChanged,
        UserNotificationType.TranslationComments,
    ];

    public static void Prepare(UserNotification notification)
    {
        if (!CombinedTypes.Contains(notification.Type))
            return;

        var parts = notification.TargetPath.Split('?', 2)[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "translations")
            return;

        notification.BatchKey = $"/translations/{parts[1]}/{parts[2]}";
        notification.Priority = NotificationPriority.Low;
    }

    public static async Task Combine(ApplicationDbContext database, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var hours = Math.Clamp(configuration.GetValue("Notifications:CombineWindowHours", 4), 1, 24);
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        var candidates = await database.UserNotifications.Where(notification => notification.ReadAt == null &&
                notification.CombinedIntoId == null && notification.EmailSentAt == null &&
                notification.CreatedAt >= cutoff && Enumerable.Contains(CombinedTypes, notification.Type))
            .ToListAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            Prepare(candidate);
        }

        foreach (var group in candidates.Where(notification => notification.BatchKey != null)
                     .GroupBy(notification => new { notification.UserId, notification.Type, notification.BatchKey }))
        {
            var ordered = group.OrderByDescending(notification => notification.CombinedCount)
                .ThenBy(notification => notification.Id).ToList();
            if (ordered.Count < 2)
                continue;

            var parent = ordered[0];
            foreach (var child in ordered.Skip(1))
            {
                // Flatten any previous batches, so marking the parent read also marks every underlying event read.
                if (child.CombinedCount > 1)
                {
                    var descendants = await database.UserNotifications.Where(notification =>
                            notification.UserId == child.UserId && notification.CombinedIntoId == child.Id)
                        .ToListAsync(cancellationToken);
                    foreach (var descendant in descendants)
                        descendant.CombinedInto = parent;
                }

                child.CombinedInto = parent;
                parent.CombinedCount += child.CombinedCount;
            }

            parent.CreatedAt = ordered.Max(notification => notification.CreatedAt);
            var action = parent.Type switch
            {
                UserNotificationType.TranslationUpdated => "translation updates",
                UserNotificationType.TranslationKeyAdded => "new translation keys",
                UserNotificationType.TranslationSourceChanged => "source text updates",
                UserNotificationType.TranslationComments => "new translation comments",
                _ => throw new InvalidOperationException("Unsupported combined notification type"),
            };
            var scope = parent.BatchKey!.Split('/');
            parent.Message = $"{parent.CombinedCount} {action} in {scope[2]} ({scope[3]}).";
            var view = parent.Type switch
            {
                UserNotificationType.TranslationComments => TranslationActivityView.Comments,
                UserNotificationType.TranslationKeyAdded => TranslationActivityView.Keys,
                UserNotificationType.TranslationSourceChanged => TranslationActivityView.Source,
                _ => TranslationActivityView.Edits,
            };
            parent.TargetPath = parent.BatchKey + "/recent?view=" + view;
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
