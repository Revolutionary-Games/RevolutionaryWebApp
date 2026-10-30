namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;
using Shared.Models;

/// <summary>
///   Marks target translations as needing review after their source text changes.
/// </summary>
public static class TranslationSourceChangeHelper
{
    public static async Task MarkTargetsOutdated(ApplicationDbContext database, TranslationProject project,
        TranslationUnit unit, CancellationToken cancellationToken)
    {
        var subscribers = await database.TranslationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.ProjectId == project.Id &&
                subscription.EventType == TranslationSubscriptionEventType.SourceChange)
            .Select(subscription => new { subscription.UserId, subscription.LanguageCode })
            .ToListAsync(cancellationToken);
        foreach (var subscriber in subscribers.OrderBy(subscription => subscription.LanguageCode)
                     .DistinctBy(subscription => subscription.UserId))
        {
            var path = $"/translations/{project.Slug}/{subscriber.LanguageCode}";
            if (database.UserNotifications.Local.Any(notification => notification.UserId == subscriber.UserId &&
                    notification.Type == UserNotificationType.TranslationSourceChanged &&
                    notification.TargetPath == path &&
                    database.Entry(notification).State == EntityState.Added))
            {
                continue;
            }

            database.UserNotifications.Add(new UserNotification
            {
                UserId = subscriber.UserId,
                Type = UserNotificationType.TranslationSourceChanged,
                DeduplicationKey = $"source-change:{project.Id}:{subscriber.LanguageCode}:{Guid.NewGuid()}",
                Message = $"Source text changed in {project.Name}. Please review translations needing changes.",
                TargetPath = path,
            });
        }

        var targets = await database.TranslationValues.Where(value => value.ProjectId == project.Id &&
                value.Key == unit.Key && value.LanguageCode != project.SourceLanguage &&
                value.State != TranslationState.NeedsTranslation)
            .ToListAsync(cancellationToken);

        foreach (var target in targets)
        {
            var priorState = target.State;
            target.State = TranslationState.NeedsTranslation;
            target.Version = Guid.NewGuid();
            if (project.Format != TranslationFormat.Manual)
            {
                target.PendingSince ??= DateTime.UtcNow;
                target.PendingRevision = ++project.CurrentRevision;
            }

            await database.TranslationChanges.AddAsync(new TranslationChange
            {
                ProjectId = project.Id,
                Key = unit.Key,
                LanguageCode = target.LanguageCode,
                Kind = TranslationChangeKind.SourceChanged,
                OldText = target.Text,
                NewText = target.Text,
                OldState = priorState,
                NewState = target.State,
                SourceRevision = unit.SourceRevision,
                ProjectRevision = project.Format == TranslationFormat.Manual ? ++project.CurrentRevision :
                    target.PendingRevision!.Value,
            }, cancellationToken);
        }
    }
}
