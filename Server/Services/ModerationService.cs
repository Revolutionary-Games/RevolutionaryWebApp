namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Utilities;
using Hangfire;
using Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Models.Translations;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Models.Pages;
using Shared.Utilities;

public class ModerationService
{
    private readonly NotificationsEnabledDb database;
    private readonly IConfiguration configuration;
    private readonly ModerationSessionService sessions;
    private readonly IBackgroundJobClient jobs;
    private readonly List<PendingUserModeration> pendingActions = new();

    public ModerationService(NotificationsEnabledDb database, IConfiguration configuration,
        ModerationSessionService sessions, IBackgroundJobClient jobs)
    {
        this.database = database;
        this.configuration = configuration;
        this.sessions = sessions;
        this.jobs = jobs;
    }

    public static bool IsModerator(User user)
    {
        var groups = user.AccessCachedGroupsOrThrow();
        return groups.HasAccessLevel(GroupType.Admin) || groups.HasGroup(GroupType.ContentModerator);
    }

    public int DefaultPurgeHours(bool admin)
    {
        return Math.Clamp(configuration.GetValue(admin ?
            "Moderation:AdminPurgeHours" :
            "Moderation:ModeratorPurgeHours", admin ? 48 : 24), 1, 128);
    }

    public async Task<DateTime?> FirstContribution(long userId)
    {
        // The budget record retains the first contribution even after content has been removed.
        var dates = new List<DateTime?>
        {
            // So we don't need to check things that count as contributions.
            /*await database.TranslationUnits.IgnoreQueryFilters().Where(unit => unit.CreatedByUserId == userId)
                .Select(unit => (DateTime?)unit.CreatedAt).MinAsync(),
            await database.TranslationValues.IgnoreQueryFilters().Where(value => value.LastEditedByUserId == userId)
                .Select(value => value.LastEditedAt).MinAsync(),
            await database.TranslationChanges.IgnoreQueryFilters().Where(change => change.ActorUserId == userId ||
                change.ContributorUserId == userId).Select(change => (DateTime?)change.CreatedAt).MinAsync(),
            await database.TranslationComments.IgnoreQueryFilters().Where(comment => comment.AuthorUserId == userId)
                .Select(comment => (DateTime?)comment.CreatedAt).MinAsync(),
            await database.TranslationSuggestions.IgnoreQueryFilters().Where(item => item.AuthorUserId == userId)
                .Select(item => (DateTime?)item.CreatedAt).MinAsync(),*/

            await database.UserContributionCounts.Where(count => count.UserId == userId)
                .Select(count => (DateTime?)count.FirstContributedAt).MinAsync(),

            await database.StorageItemVersions.Where(item => item.UploadedById == userId)
                .Select(item => (DateTime?)item.CreatedAt).MinAsync(),
            await database.MediaFiles.Where(item => item.UploadedById == userId)
                .Select(item => (DateTime?)item.CreatedAt).MinAsync(),
            await database.VersionedPages.Where(item => item.CreatorId == userId)
                .Select(item => (DateTime?)item.CreatedAt).MinAsync(),
            await database.DictionaryTerms.IgnoreQueryFilters().Where(item => item.LastEditedByUserId == userId)
                .Select(item => (DateTime?)item.CreatedAt).MinAsync(),
        };
        return dates.Min();
    }

    public async Task<string?> ValidateAction(User actor, UserReport report, ResolveUserReportRequest request)
    {
        if (!Enum.IsDefined(request.Resolution) || !Enum.IsDefined(request.Action))
            return "Invalid moderation action";

        if (request.Resolution == UserReportResolution.Dismissed &&
            (request.Action != ModerationAction.Resolve || request.DeleteContent))
        {
            return "Rejecting a report cannot punish the reported user";
        }

        if (string.IsNullOrWhiteSpace(request.Note))
            return "A moderation note is required";
        if (request.Action == ModerationAction.Resolve && !request.DeleteContent)
            return null;
        if (report.TargetUserId == null)
            return "The reported account has been deleted";
        if (report.TargetUserId == actor.Id)
            return "You cannot moderate your own reports";

        var target = await database.Users.Include(user => user.Groups)
            .SingleAsync(user => user.Id == report.TargetUserId);
        var groups = target.ProcessGroupDataFromLoadedGroups();

        var admin = actor.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Admin);
        if (groups.HasAccessLevel(GroupType.Admin) || (!admin && groups.HasGroup(GroupType.ContentModerator)))
            return "This account requires an administrator to review it; administrators cannot be banned here";
        if (request.Days is < 1 or > 3650)
            return "Duration must be between 1 and 3650 days";

        if (request.Action == ModerationAction.PurgeAndBan)
        {
            var first = await FirstContribution(target.Id);
            if (!admin && (first == null || first <= DateTime.UtcNow.AddHours(-72)))
                return "Content moderators can only purge users whose first contribution was less than 72 hours ago";
            if (!admin && request.AllTime)
                return "Only administrators can purge all time";

            var hours = request.PurgeHours ?? DefaultPurgeHours(admin);
            if (hours is < 1 or > 128 || (!admin && hours > DefaultPurgeHours(false)))
                return "The purge window exceeds your permitted range";
        }

        return null;
    }

    public async Task<string> ApplyAction(User actor, UserReport report, ResolveUserReportRequest request)
    {
        var outcome = "Report resolved.";
        var staged = request.Action is ModerationAction.Ban or ModerationAction.PurgeAndBan;
        if (!staged && (request.DeleteContent || request.Action == ModerationAction.DeleteContent))
        {
            var retained = await DeleteReportedContent(actor, report);
            if (retained > 0)
                outcome = "Report resolved. The translation edit was retained because a later edit takes precedence.";
        }

        if (request.Action is ModerationAction.Mute or ModerationAction.Ban or ModerationAction.PurgeAndBan)
        {
            var target = await database.Users.Include(user => user.Groups)
                .SingleAsync(user => user.Id == report.TargetUserId);
            target.ProcessGroupDataFromLoadedGroups();
            if (request.Action == ModerationAction.Mute)
            {
                var until = DateTime.UtcNow.AddDays(request.Days);
                if (target.MutedUntil == null || target.MutedUntil < until)
                    target.MutedUntil = until;
            }
            else
            {
                var until = request.Permanent || request.Action == ModerationAction.PurgeAndBan ?
                    DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) :
                    DateTime.UtcNow.AddDays(request.Days);
                DateTime? cutoff = null;
                if (request.Action == ModerationAction.PurgeAndBan)
                {
                    var admin = actor.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Admin);
                    var hours = request.PurgeHours ?? DefaultPurgeHours(admin);
                    cutoff = request.AllTime ?
                        DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) :
                        DateTime.UtcNow.AddHours(-hours);
                }

                await StageBan(actor, target, until, request.Note, cutoff, request.DeleteContent ? report.Id : null);
                outcome = "User restricted immediately. Ban and any purge will run in five seconds, " +
                    "with a second cleanup pass four minutes later. Results are recorded in the admin action log.";
            }
        }

        return outcome;
    }

    public async Task StageBan(User actor, User target, DateTime until, string reason, DateTime? purgeCutoff = null,
        long? reportId = null)
    {
        target.ModerationPending = true;
        var action = new PendingUserModeration
        {
            TargetUserId = target.Id, ActorUserId = actor.Id, BanUntil = until,
            PurgeCutoff = purgeCutoff, Reason = reason, ReportId = reportId,
        };
        await database.PendingUserModeration.AddAsync(action);
        pendingActions.Add(action);
    }

    public void SchedulePendingActions()
    {
        foreach (var action in pendingActions)
        {
            jobs.Schedule<ApplyUserModerationJob>(job => job.Execute(action.Id, false, CancellationToken.None),
                TimeSpan.FromSeconds(5));
            jobs.Schedule<ApplyUserModerationJob>(job => job.Execute(action.Id, true, CancellationToken.None),
                TimeSpan.FromMinutes(4));
        }

        pendingActions.Clear();
    }

    public async Task ProcessPendingAction(long actionId, bool cleanup, CancellationToken cancellationToken)
    {
        var initial = await database.PendingUserModeration.AsNoTracking()
            .SingleOrDefaultAsync(action => action.Id == actionId, cancellationToken);
        if (initial?.TargetUserId == null)
            return;

        await using var userLock = await UserAdvisoryLock.AcquireAsync(database, initial.TargetUserId.Value,
            cancellationToken);
        var locks = new List<TranslationProjectAdvisoryLock>();
        try
        {
            if (initial.PurgeCutoff != null || initial.ReportId != null)
            {
                var projectIds = await database.TranslationChanges.IgnoreQueryFilters()
                    .Where(change => change.ActorUserId == initial.TargetUserId ||
                        change.ContributorUserId == initial.TargetUserId)
                    .Select(change => change.ProjectId).Union(database.TranslationUnits.IgnoreQueryFilters()
                        .Where(unit => unit.CreatedByUserId == initial.TargetUserId).Select(unit => unit.ProjectId))
                    .Distinct().OrderBy(id => id).ToListAsync(cancellationToken);
                foreach (var projectId in projectIds)
                {
                    locks.Add(await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId,
                        cancellationToken));
                }
            }

            await using var transaction = database.Database.IsRelational() ?
                await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) :
                null;
            var action = await database.PendingUserModeration.Include(item => item.TargetUser)
                .ThenInclude(user => user!.Groups).Include(item => item.ActorUser)
                .SingleAsync(item => item.Id == actionId, cancellationToken);
            if (action.TargetUser == null || action.CleanedUpAt != null || (!cleanup && action.AppliedAt != null))
                return;

            var target = action.TargetUser;
            target.ProcessGroupDataFromLoadedGroups();
            if (action.AppliedAt != null && !target.Suspended)
            {
                // A manual unsuspension cancels the remaining clean-up.
                action.CleanedUpAt = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);
                return;
            }

            var skipped = 0;
            if (action.AppliedAt == null)
            {
                if (target.SuspendedUntil == null || target.SuspendedUntil < action.BanUntil)
                    target.SuspendedUntil = action.BanUntil;

                target.SuspendedManually = true;
                target.SuspendedReason = action.Reason;
                action.AppliedAt = DateTime.UtcNow;
            }

            // Keep the original window, including on retries and the second pass.
            if (action.PurgeCutoff != null)
            {
                await BlockRegistration(target, action.Id);
                skipped = await PurgeActivity(action.ActorUser, target, action.PurgeCutoff.Value);
            }

            if (action.ReportId != null)
            {
                var report = await database.UserReports.SingleOrDefaultAsync(item => item.Id == action.ReportId,
                    cancellationToken);
                if (report != null)
                    skipped += await DeleteReportedContent(action.ActorUser, report);
            }

            await sessions.RevokeSessions(target.Id);
            target.ModerationPending = await database.PendingUserModeration.AnyAsync(other =>
                    other.Id != action.Id && other.TargetUserId == target.Id && other.AppliedAt == null,
                cancellationToken);
            if (cleanup)
                action.CleanedUpAt = DateTime.UtcNow;

            await database.AdminActions.AddAsync(new AdminAction($"Staged moderation {action.Id}: " +
                (cleanup ? "second cleanup completed" : "ban and cleanup completed"),
                $"{action.Reason}\nPurge cutoff: {action.PurgeCutoff:u}; edits retained: {skipped}")
            {
                PerformedById = action.ActorUserId, TargetUserId = target.Id,
            }, cancellationToken);

            var sender = database.AutoSendNotifications;
            var notifications = database.RunPreSaveChecks();
            database.AutoSendNotifications = null;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                database.AutoSendNotifications = sender;
            }

            await sessions.CompleteRevocation();
            if (sender != null && notifications != null)
                await sender.SendNotifications(notifications);
        }
        finally
        {
            for (var index = locks.Count - 1; index >= 0; --index)
                await locks[index].DisposeAsync();
            database.SuppressModerationJournal = false;
        }
    }

    public async Task UpdateCommentVisibility(UserReport report)
    {
        if (report.Target != UserReportTarget.TranslationComment)
            return;

        var comment = await database.TranslationComments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == report.TargetId);
        if (comment == null)
            return;

        comment.Reported = await database.UserReports.AnyAsync(other => other.Id != report.Id &&
            other.Target == report.Target && other.TargetId == report.TargetId && other.ResolvedAt == null);

        // Also emit a realtime refresh when a second report leaves the flag unchanged.
        database.Entry(comment).Property(item => item.Reported).IsModified = true;
    }

    private async Task<int> DeleteReportedContent(User? actor, UserReport report)
    {
        if (report.Target == UserReportTarget.TranslationComment)
        {
            var comment = await database.TranslationComments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == report.TargetId);
            comment?.Deleted = true;
        }
        else if (report.Target == UserReportTarget.TranslationChange)
        {
            var change = await database.TranslationChanges.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == report.TargetId);
            if (change != null)
                return await RevertTranslations(actor, new List<TranslationChange> { change });
        }

        return 0;
    }

    private async Task<int> RevertTranslations(User? actor, List<TranslationChange> changes)
    {
        var skipped = 0;
        foreach (var group in changes.GroupBy(change => new { change.ProjectId, change.Key, change.LanguageCode }))
        {
            // Callers hold all affected project advisory locks for the transaction.
            var value = await database.TranslationValues.IgnoreQueryFilters().Include(item => item.Unit)
                .Include(item => item.Problems)
                .Include(item => item.Language).ThenInclude(language => language.Project)
                .SingleOrDefaultAsync(item => item.ProjectId == group.Key.ProjectId && item.Key == group.Key.Key &&
                    item.LanguageCode == group.Key.LanguageCode);
            if (value == null)
                continue;

            var project = value.Language.Project;
            var oldText = value.Text;
            var oldState = value.State;
            var oldExplanation = value.Unit.Explanation;
            var candidates = group.Select(change => change.Id).ToHashSet();
            var newerHistory = await database.TranslationChanges.IgnoreQueryFilters().AsNoTracking()
                .Where(change => change.ProjectId == value.ProjectId && change.Key == value.Key &&
                    change.LanguageCode == value.LanguageCode && change.RevertedAt == null)
                .OrderByDescending(change => change.CreatedAt).ThenByDescending(change => change.Id).ToListAsync();

            // A previous purge must not block undoing a write that finished after that purge.
            var barrier = newerHistory.FirstOrDefault(change => !candidates.Contains(change.Id) &&
                change.Kind != TranslationChangeKind.ModerationRevert);
            foreach (var change in group.OrderByDescending(change => change.CreatedAt)
                         .ThenByDescending(change => change.Id))
            {
                if ((barrier != null && (barrier.CreatedAt > change.CreatedAt ||
                        (barrier.CreatedAt == change.CreatedAt && barrier.Id > change.Id))) ||
                    value.Text != change.NewText || value.State != change.NewState ||
                    (change.OldExplanation != change.NewExplanation &&
                        value.Unit.Explanation != change.NewExplanation))
                {
                    if (change.RevertedAt == null)
                        ++skipped;
                    continue;
                }

                value.Text = change.OldText;
                value.State = change.OldState;
                if (change.OldExplanation != change.NewExplanation)
                    value.Unit.Explanation = change.OldExplanation;
                change.RevertedAt = DateTime.UtcNow;
            }

            if (oldText == value.Text && oldState == value.State && oldExplanation == value.Unit.Explanation)
                continue;

            value.LastEditedByUserId = actor?.Id;
            value.LastEditedAt = DateTime.UtcNow;
            value.Version = Guid.NewGuid();
            var revision = ++project.CurrentRevision;
            if (value.LanguageCode == project.SourceLanguage && oldText != value.Text)
            {
                value.Unit.SourceText = value.Text;
                ++value.Unit.SourceRevision;
                value.Unit.SourceUpdatedAt = DateTime.UtcNow;
                if (project.Format == TranslationFormat.Manual)
                {
                    value.Unit.SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Text)));
                    value.LastImportedText = value.Text;
                }

                await TranslationSourceChangeHelper.MarkTargetsOutdated(database, project, value.Unit,
                    CancellationToken.None);
            }

            value.SourceRevisionTranslatedFrom = value.Unit.SourceRevision;
            value.SourceTextTranslatedFrom = value.Unit.SourceText;
            if (project.Format != TranslationFormat.Manual)
            {
                value.PendingRevision = revision;
                value.PendingSince ??= DateTime.UtcNow;
            }

            await TranslationQualityChecks.UpdateAfterEdit(database, project, value, CancellationToken.None);

            await database.TranslationChanges.AddAsync(new TranslationChange
            {
                ProjectId = value.ProjectId, Key = value.Key, LanguageCode = value.LanguageCode,
                ActorUserId = actor?.Id, Kind = TranslationChangeKind.ModerationRevert,
                OldText = oldText, NewText = value.Text, OldState = oldState, NewState = value.State,
                OldExplanation = oldExplanation, NewExplanation = value.Unit.Explanation,
                SourceRevision = value.Unit.SourceRevision, ProjectRevision = revision,
            });
        }

        return skipped;
    }

    private async Task<int> PurgeActivity(User? actor, User target, DateTime cutoff)
    {
        database.SuppressModerationJournal = true;
        var skipped = await ModerationRevisionJournal.Revert(database, target.Id, cutoff);
        skipped += await RevertTranslations(actor, await database.TranslationChanges.IgnoreQueryFilters()
            .Where(change => (change.ActorUserId == target.Id || change.ContributorUserId == target.Id) &&
                change.CreatedAt >= cutoff).ToListAsync());
        foreach (var comment in await database.TranslationComments.IgnoreQueryFilters()
                     .Where(item => item.AuthorUserId == target.Id && item.CreatedAt >= cutoff).ToListAsync())
        {
            comment.Deleted = true;
        }

        foreach (var suggestion in await database.TranslationSuggestions.IgnoreQueryFilters()
                     .Where(item => item.AuthorUserId == target.Id && item.CreatedAt >= cutoff).ToListAsync())
        {
            database.TranslationSuggestions.Remove(suggestion);
            suggestion.ResolvedAt = DateTime.UtcNow;
            suggestion.ResolvedByUserId = actor?.Id;
        }

        database.TranslationSuggestionVotes.RemoveRange(await database.TranslationSuggestionVotes.IgnoreQueryFilters()
            .Where(item => item.UserId == target.Id && item.CreatedAt >= cutoff).ToListAsync());
        foreach (var request in await database.DictionaryTermRequests.IgnoreQueryFilters()
                     .Where(item => item.RequesterUserId == target.Id && item.CreatedAt >= cutoff).ToListAsync())
        {
            request.Approved = false;
            request.DecidedByUserId = actor?.Id;
            request.DecidedAt = DateTime.UtcNow;
        }

        foreach (var item in await database.DictionaryTerms.IgnoreQueryFilters()
                     .Where(item => item.LastEditedByUserId == target.Id && item.UpdatedAt >= cutoff).ToListAsync())
        {
            // Journal replay may already have restored an earlier editor in this tracked instance.
            if (item.LastEditedByUserId != target.Id || item.UpdatedAt < cutoff)
                continue;

            if (item.CreatedAt < cutoff)
            {
                ++skipped;
                continue;
            }

            item.Deleted = true;
        }

        foreach (var item in await database.DictionaryLanguageTerms.IgnoreQueryFilters()
                     .Where(item => item.LastEditedByUserId == target.Id && item.UpdatedAt >= cutoff).ToListAsync())
        {
            // Journal replay may already have restored an earlier editor in this tracked instance.
            if (item.LastEditedByUserId != target.Id || item.UpdatedAt < cutoff)
                continue;

            if (item.CreatedAt < cutoff)
            {
                ++skipped;
                continue;
            }

            item.Deleted = true;
        }

        foreach (var item in await database.DictionaryTermTranslations.IgnoreQueryFilters()
                     .Where(item => item.LastEditedByUserId == target.Id && item.UpdatedAt >= cutoff).ToListAsync())
        {
            // Journal replay may already have restored an earlier editor in this tracked instance.
            if (item.LastEditedByUserId != target.Id || item.UpdatedAt < cutoff)
                continue;

            if (item.CreatedAt < cutoff)
            {
                ++skipped;
                continue;
            }

            database.DictionaryTermTranslations.Remove(item);
        }

        foreach (var unit in await database.TranslationUnits.IgnoreQueryFilters().Where(item =>
                     item.CreatedByUserId == target.Id && item.CreatedAt >= cutoff).ToListAsync())
        {
            unit.Active = false;
        }

        foreach (var item in await database.MediaFiles.Where(item => item.UploadedById == target.Id &&
                     item.CreatedAt >= cutoff).ToListAsync())
        {
            item.Deleted = true;
            item.UpdatedAt = DateTime.UtcNow;
            foreach (var size in Enum.GetValues<MediaFileSize>())
            {
                await QueueUploadDeletion(MediaFileExtensions.GetStoragePath(item, size),
                    ModerationUploadStorage.Media);
            }

            await QueueUploadDeletion(item.GetUploadPath(), ModerationUploadStorage.Temporary);
            await QueueUploadDeletion(item.GetIntermediateProcessingPath(), ModerationUploadStorage.Temporary);
        }

        foreach (var item in await database.StorageItemVersions.Include(item => item.StorageFile)
                     .Where(item => item.UploadedById == target.Id && item.CreatedAt >= cutoff).ToListAsync())
        {
            item.Deleted = true;
            item.Keep = false;
            item.UpdatedAt = DateTime.UtcNow;
            if (item.StorageFile != null)
            {
                await QueueUploadDeletion(item.StorageFile.StoragePath, ModerationUploadStorage.General);
                await QueueUploadDeletion(item.StorageFile.UploadPath, ModerationUploadStorage.General);
            }
        }

        foreach (var item in await database.StorageItems.Include(item => item.DeleteInfo)
                     .Where(item => item.OwnerId == target.Id && item.CreatedAt >= cutoff && !item.Special)
                     .ToListAsync())
        {
            item.DeleteInfo ??= new StorageItemDeleteInfo(item, "Removed by moderation") { DeletedById = actor?.Id };
            item.Deleted = true;
            item.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var item in await database.VersionedPages.Where(item => item.CreatorId == target.Id &&
                     item.CreatedAt >= cutoff).ToListAsync())
        {
            item.Deleted = true;
            item.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in database.ChangeTracker.Entries<Models.Pages.VersionedPage>()
                     .Where(entry => entry.State == EntityState.Modified && !entry.Entity.Deleted).ToList())
        {
            var page = entry.Entity;
            var original = entry.Property(item => item.LatestContent).OriginalValue;
            if (original != page.LatestContent)
            {
                var previous = page.CreatePreviousVersion(await page.GetCurrentVersion(database), original);
                previous.EditedById = entry.Property(item => item.LastEditorId).OriginalValue;
                previous.EditComment = "Content before moderation purge";
                await database.PageVersions.AddAsync(previous);
            }

            page.LastEditorId = actor?.Id;
            page.LastEditComment = "Reverted by moderation purge";
            page.UpdatedAt = DateTime.UtcNow;
        }

        database.PageEditSuggestions.RemoveRange(await database.PageEditSuggestions.Where(item =>
            item.SuggestedById == target.Id && item.CreatedAt >= cutoff).ToListAsync());
        return skipped;
    }

    private async Task QueueUploadDeletion(string path, ModerationUploadStorage storage)
    {
        await database.ModerationUploadDeletions.AddAsync(new ModerationUploadDeletion
        {
            Path = path, Storage = storage,
        });
        await database.ModerationUploadDeletions.AddAsync(new ModerationUploadDeletion
        {
            Path = path, Storage = storage, DeleteAfter = DateTime.UtcNow + AppInfo.RemoteStorageUploadExpireTime * 2,
        });
    }

    private async Task BlockRegistration(User target, long moderationActionId)
    {
        var addresses = (await database.Sessions.Where(session => session.UserId == target.Id)
                .Select(session => session.LastUsedFrom).ToListAsync())
            .Select(RegistrationBlocks.NormalizeAddress).OfType<string>().ToHashSet();
        if (target.LastKnownIPAddress != null)
            addresses.Add(target.LastKnownIPAddress);

        var email = Normalization.NormalizeEmail(target.Email);
        var existingBlocks = await database.RegistrationBlocks.Where(block =>
                block.NormalizedEmail == email || (block.IPAddress != null && addresses.Contains(block.IPAddress)))
            .Select(block => new { block.NormalizedEmail, block.IPAddress }).ToListAsync();
        if (existingBlocks.All(block => block.NormalizedEmail != email))
        {
            database.RegistrationBlocks.Add(new RegistrationBlock
            {
                Email = target.Email, NormalizedEmail = email, TargetUserId = target.Id,
                ModerationActionId = moderationActionId,
            });
        }

        addresses.ExceptWith(existingBlocks.Select(block => block.IPAddress).OfType<string>());
        foreach (var address in addresses)
        {
            database.RegistrationBlocks.Add(new RegistrationBlock
            {
                IPAddress = address, TargetUserId = target.Id, ModerationActionId = moderationActionId,
            });
        }
    }
}
