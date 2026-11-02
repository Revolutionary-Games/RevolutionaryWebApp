namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;
using Shared.Models;
using Shared.Models.Enums;

/// <summary>
///   Maintenance transitions performed while holding the project's advisory lock.
///   The caller saves notices and state together.
/// </summary>
public static class TranslationMaintenance
{
    public static async Task ConflictDetected(ApplicationDbContext database, TranslationProject project)
    {
        project.ConflictClosed = true;
        project.UpdatedAt = DateTime.UtcNow;
        var admins = await database.Users.Where(user => user.Groups.Any(group => group.Id == GroupType.Admin))
            .Select(user => user.Id).ToListAsync();
        var eventId = Guid.NewGuid();
        foreach (var admin in admins)
        {
            database.UserNotifications.Add(new UserNotification
            {
                UserId = admin,
                DeduplicationKey = $"translation-conflict:{eventId}:{admin}",
                Type = UserNotificationType.TranslationImportConflict,
                Priority = NotificationPriority.High,
                Message = $"A new translation conflict in {project.Name} blocks publishing. " +
                    "The project is closed for maintenance. Resolve the conflicts to resume translations and pushes.",
                TargetPath = "/admin/translations",
            });
        }
    }

    public static async Task NotifyReopened(ApplicationDbContext database, TranslationProject project)
    {
        if (project.ClosedForMaintenance)
            return;

        var subscriptions = await database.TranslationSubscriptions.Where(item => item.ProjectId == project.Id &&
            item.EventType == TranslationSubscriptionEventType.ProjectReopened).ToListAsync();
        var eventId = Guid.NewGuid();
        foreach (var userId in subscriptions.Select(item => item.UserId).Distinct())
        {
            database.UserNotifications.Add(new UserNotification
            {
                UserId = userId,
                DeduplicationKey = $"translation-reopened:{eventId}:{userId}",
                Type = UserNotificationType.TranslationProjectReopened,
                Message = $"{project.Name} is available for translation again.",
                TargetPath = $"/translations/{Uri.EscapeDataString(project.Slug)}",
            });
        }

        // Opening subscriptions trigger just once
        database.TranslationSubscriptions.RemoveRange(subscriptions);
    }
}
