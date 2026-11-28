namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Npgsql;
using Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class UserReportsController : Controller
{
    private readonly NotificationsEnabledDb database;
    private readonly ModerationService moderation;
    private readonly ModerationSessionService sessions;
    private readonly ILogger<UserReportsController> logger;

    public UserReportsController(NotificationsEnabledDb database, ModerationService moderation,
        ModerationSessionService sessions,
        ILogger<UserReportsController> logger)
    {
        this.database = database;
        this.moderation = moderation;
        this.sessions = sessions;
        this.logger = logger;
    }

    [HttpGet]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.ContentModerator, AllowAdmin = true)]
    public async Task<ActionResult<PagedResult<UserReportDTO>>> List(string sortColumn, SortDirection sortDirection,
        int page, int pageSize, bool solved = false)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest("Invalid pagination");

        var query = database.UserReports.AsNoTracking().Where(report => solved || report.ResolvedAt == null);
        try
        {
            var result = await query.OrderBy(sortColumn, sortDirection).ToPagedResultAsync(page, pageSize);
            return result.ConvertResult(report => report.GetDTO(false));
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Invalid moderation queue sort: {Column}", sortColumn);
            throw new HttpResponseException { Value = "Invalid sort column" };
        }
    }

    [HttpGet("{id:long}")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.ContentModerator, AllowAdmin = true)]
    public async Task<ActionResult<UserReportDTO>> Get(long id)
    {
        var report = await database.UserReports.AsNoTracking().Include(item => item.Notes)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (report == null)
            return NotFound();

        var dto = report.GetDTO();
        var admin = HttpContext.AuthenticatedUserOrThrow().AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Admin);

        dto.FirstContributedAt = report.TargetUserId == null ? null :
            await moderation.FirstContribution(report.TargetUserId.Value);
        dto.CanPurge = report.TargetUserId != null && (admin || dto.FirstContributedAt > DateTime.UtcNow.AddHours(-72));
        dto.DefaultPurgeHours = moderation.DefaultPurgeHours(admin);
        return dto;
    }

    [HttpPost]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<ActionResult<UserReportDTO>> Create([FromBody] CreateUserReportRequest request)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();
        if (!Enum.IsDefined(request.Target) || !Enum.IsDefined(request.Reason))
            return BadRequest("Invalid report target or reason");
        if (request.Reason == UserReportReason.Other && string.IsNullOrWhiteSpace(request.Details))
            return BadRequest("Please describe the reason for this report");

        var report = new UserReport
        {
            Target = request.Target, TargetId = request.TargetId, Reason = request.Reason,
            ReporterUserId = actor.Id, Details = request.Details.Trim(),
        };
        TranslationComment? comment = null;
        if (request.Target == UserReportTarget.User)
        {
            report.TargetUserId = request.TargetId;
        }
        else if (request.Target == UserReportTarget.TranslationComment)
        {
            comment = await database.TranslationComments.FirstOrDefaultAsync(item => item.Id == request.TargetId &&
                !item.Deleted && item.Value.Language.Project.Enabled);
            if (comment == null)
                return NotFound();

            report.TargetUserId = comment.AuthorUserId;
            report.ContentSnapshot = comment.RawMarkdown;
            report.ContentPath = await TranslationContentPath(comment.ProjectId, comment.LanguageCode, comment.Key);
        }
        else
        {
            var change = await database.TranslationChanges.AsNoTracking().FirstOrDefaultAsync(item =>
                item.Id == request.TargetId && item.Value.Language.Project.Enabled);
            if (change == null)
                return NotFound();

            report.TargetUserId = change.ActorUserId;
            report.ContentSnapshot = $"Explanation: {change.OldExplanation} → {change.NewExplanation}\n" +
                $"{change.OldState} → {change.NewState}\n{change.OldText}\n→\n{change.NewText}";
            report.ContentPath = await TranslationContentPath(change.ProjectId, change.LanguageCode, change.Key);
        }

        if (report.TargetUserId == null)
            return BadRequest("This edit does not have a reportable author");

        var target = await database.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == report.TargetUserId);
        if (target == null)
            return NotFound();
        if (target.Id == actor.Id)
            return BadRequest("You cannot report yourself");

        report.TargetUserName = target.UserName;

        // Serialise report creation and resolution for the same target account.
        await using var userLock = await UserAdvisoryLock.AcquireAsync(database, target.Id);
        if (await database.UserReports.AnyAsync(item => item.ReporterUserId == actor.Id &&
                item.Target == request.Target && item.TargetId == request.TargetId && item.ResolvedAt == null))
        {
            return Conflict("You have already reported this content; it is awaiting review");
        }

        // TODO: higher limit for older and more trusted users
        var cutoff = DateTime.UtcNow.AddHours(-24);
        if (await database.UserReports.CountAsync(item => item.ReporterUserId == actor.Id &&
                item.CreatedAt >= cutoff) >= 20)
        {
            return StatusCode(429, "Daily report limit reached");
        }

        if (comment != null)
        {
            comment.Reported = true;
            database.Entry(comment).Property(item => item.Reported).IsModified = true;
        }

        await database.UserReports.AddAsync(report);
        await database.SaveChangesAsync();
        logger.LogInformation("User report {ReportId} created by {ActorId} for {TargetId}",
            report.Id, actor.Id, target.Id);

        // Report details and moderator notes are never returned to the reporter.
        return Created($"/api/v1/UserReports/{report.Id}", report.GetDTO(false));
    }

    [HttpPost("{id:long}/notes")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.ContentModerator, AllowAdmin = true)]
    public async Task<IActionResult> AddNote(long id, [FromBody] UserReportNoteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
            return BadRequest("Note cannot be empty");

        var report = await database.UserReports.SingleOrDefaultAsync(item => item.Id == id);
        if (report == null)
            return NotFound();

        var actor = HttpContext.AuthenticatedUserOrThrow();
        await database.UserReportNotes.AddAsync(new UserReportNote
        {
            ReportId = id, AuthorUserId = actor.Id, Text = request.Note.Trim(),
        });
        await database.AdminActions.AddAsync(new AdminAction($"Note attached to user report {id}", request.Note)
        {
            PerformedById = actor.Id, TargetUserId = report.TargetUserId,
        });
        await database.SaveChangesAsync();

        logger.LogInformation("Moderator {Actor} added note to report {ReportId}", actor.Email, id);
        return Ok();
    }

    [HttpPost("{id:long}/resolve")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.ContentModerator, AllowAdmin = true)]
    public async Task<IActionResult> Resolve(long id, [FromBody] ResolveUserReportRequest request)
    {
        var initial = await database.UserReports.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
        if (initial == null)
            return NotFound();

        var actor = HttpContext.AuthenticatedUserOrThrow();
        await using var userLock = await UserAdvisoryLock.AcquireAsync(database, initial.TargetUserId ?? actor.Id);
        var projectIds = await database.TranslationChanges.IgnoreQueryFilters().Where(change =>
                initial.Target == UserReportTarget.TranslationChange && change.Id == initial.TargetId &&
                request.Action != ModerationAction.Ban && request.Action != ModerationAction.PurgeAndBan)
            .Select(change => change.ProjectId).Distinct().OrderBy(projectId => projectId).ToListAsync();

        var locks = new List<TranslationProjectAdvisoryLock>();
        try
        {
            foreach (var projectId in projectIds)
                locks.Add(await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId));

            await using var transaction = database.Database.IsRelational() ?
                await database.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

            var report = await database.UserReports.SingleAsync(item => item.Id == id);
            if (report.ResolvedAt != null)
                return Conflict("This report has already been resolved");

            var error = await moderation.ValidateAction(actor, report, request);
            if (error != null)
                return BadRequest(error);

            if (request.Action == ModerationAction.PurgeAndBan &&
                actor.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Admin) &&
                (request.AllTime || request.PurgeHours > moderation.DefaultPurgeHours(true)))
            {
                var sudoRequired = await RequireSudoModeAttribute.CheckSudoMode(HttpContext);
                if (sudoRequired != null)
                    return sudoRequired;
            }

            var outcome = await moderation.ApplyAction(actor, report, request);
            report.Resolution = request.Resolution;
            report.ResolvedAt = DateTime.UtcNow;
            report.ResolvedByUserId = actor.Id;

            // Thank the user who made the report on approving it
            if (request.Resolution == UserReportResolution.Approved && report.ReporterUserId is { } reporterId)
            {
                await database.UserNotifications.AddAsync(new UserNotification
                {
                    UserId = reporterId,
                    DeduplicationKey = $"user-report:{id}:approved",
                    Type = UserNotificationType.UserReportApproved,
                    Message = "Site moderation has taken action based on your report. " +
                        "Thank you for helping keep the site clean!",
                    TargetPath = "/userNotifications",
                });
            }

            await database.UserReportNotes.AddAsync(new UserReportNote
            {
                ReportId = id, AuthorUserId = actor.Id, Text = request.Note.Trim(),
            });
            await moderation.UpdateCommentVisibility(report);
            await database.AdminActions.AddAsync(new AdminAction(
                $"User report {id}: {request.Resolution}, action {request.Action}",
                $"{request.Note}\nDays: {request.Days}; permanent: {request.Permanent}; " +
                $"purge hours: {request.PurgeHours}; all time: {request.AllTime}; " +
                $"delete content: {request.DeleteContent}\n{outcome}")
            {
                PerformedById = actor.Id, TargetUserId = report.TargetUserId,
            });

            // Publish realtime changes only after the moderation transaction commits.
            var sender = database.AutoSendNotifications;
            var notifications = database.RunPreSaveChecks();
            database.AutoSendNotifications = null;
            try
            {
                await database.SaveChangesAsync();
                if (transaction != null)
                    await transaction.CommitAsync();
            }
            finally
            {
                database.AutoSendNotifications = sender;
            }

            try
            {
                moderation.SchedulePendingActions();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not schedule moderation for report {ReportId}; " +
                    "the recovery job will process the saved action", id);
            }

            try
            {
                if (sender != null && notifications != null)
                    await sender.SendNotifications(notifications);
                await sessions.CompleteRevocation();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to notify clients after revoking sessions for report {ReportId}",
                    id);
            }

            logger.LogInformation("Moderator {Actor} resolved report {ReportId} with {Action}",
                actor.Email, id, request.Action);
            return Ok(outcome);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Content changed while moderating. Reload and review it again.");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return Conflict("Content changed while moderating. Reload and review it again.");
        }
        finally
        {
            for (var index = locks.Count - 1; index >= 0; --index)
                await locks[index].DisposeAsync();

            database.SuppressModerationJournal = false;
        }
    }

    private async Task<string> TranslationContentPath(long projectId, string language, string key)
    {
        var slug = await database.TranslationProjects.Where(project => project.Id == projectId)
            .Select(project => project.Slug).SingleAsync();
        return $"/translations/{slug}/{language}/edit?key={Uri.EscapeDataString(key)}";
    }
}
