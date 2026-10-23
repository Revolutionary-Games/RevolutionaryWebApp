namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Models;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/admin/translationProjects")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
public class AdminTranslationProjectsController : Controller
{
    private readonly ILogger<AdminTranslationProjectsController> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobs;

    public AdminTranslationProjectsController(ILogger<AdminTranslationProjectsController> logger,
        ApplicationDbContext database, IBackgroundJobClient jobs)
    {
        this.logger = logger;
        this.database = database;
        this.jobs = jobs;
    }

    [HttpGet]
    public async Task<ActionResult<TranslationProjectConfigurationDTO[]>> List()
    {
        var rows = await database.TranslationProjects.AsNoTracking().OrderBy(p => p.Name)
            .ToListAsync();
        return rows.Select(project => project.GetConfigurationDTO()).ToArray();
    }

    [HttpPost]
    public async Task<ActionResult<TranslationProjectConfigurationDTO>> Create(
        [FromBody] TranslationProjectConfigurationDTO request)
    {
        if (!TryValidate(request, out var format, out var error))
            return BadRequest(error);

        if (await database.TranslationProjects.AnyAsync(p => p.Slug == request.Slug))
            return Conflict("Project slug already exists");

        var project = new TranslationProject();
        Apply(project, request, format);
        await database.TranslationProjects.AddAsync(project);
        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction($"Translation project {request.Slug} created")
        {
            PerformedById = user.Id,
        });
        await database.SaveChangesAsync();
        if (format == TranslationFormat.Manual)
        {
            await database.TranslationProjectLanguages.AddAsync(new TranslationProjectLanguage
            {
                ProjectId = project.Id, Project = project, LanguageCode = project.SourceLanguage,
                DisplayName = project.SourceLanguage,
            });
            await database.SaveChangesAsync();
        }

        logger.LogInformation("Translation project {Slug} created by {Email}", project.Slug, user.Email);
        return CreatedAtAction(nameof(List), project.GetConfigurationDTO());
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<TranslationProjectConfigurationDTO>> Update(long id,
        [FromBody] TranslationProjectConfigurationDTO request)
    {
        if (!TryValidate(request, out var format, out var error))
            return BadRequest(error);

        var project = await database.TranslationProjects.FindAsync(id);
        if (project == null)
            return NotFound();

        if (project.Format != format || project.SourceLanguage != request.SourceLanguage)
            return BadRequest("Changing an existing project's format or source language is not supported");

        if (await database.TranslationProjects.AnyAsync(p => p.Slug == request.Slug && p.Id != id))
        {
            return Conflict("Project slug already exists");
        }

        Apply(project, request, format);
        project.BumpUpdatedAt();
        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction($"Translation project {request.Slug} updated")
        {
            PerformedById = user.Id,
        });
        await database.SaveChangesAsync();
        logger.LogInformation("Translation project {Slug} updated by {Email}", project.Slug, user.Email);
        return project.GetConfigurationDTO();
    }

    [HttpPost("{id:long}/import")]
    public async Task<IActionResult> QueueImport(long id)
    {
        if (!await database.TranslationProjects.AnyAsync(p => p.Id == id && p.Enabled))
            return NotFound();

        jobs.Enqueue<ImportTranslationProjectJob>(job => job.Execute(id, CancellationToken.None));
        logger.LogInformation("Translation project import queued for {ProjectId} by {Email}", id,
            HttpContext.AuthenticatedUserOrThrow().Email);
        return Accepted();
    }

    [HttpPost("{id:long}/publish")]
    public async Task<IActionResult> QueuePublish(long id)
    {
        if (!await database.TranslationProjects.AnyAsync(p => p.Id == id && p.Enabled &&
                p.RepositoryFullName != null))
        {
            return NotFound();
        }

        jobs.Enqueue<PublishTranslationProjectJob>(job => job.Execute(id, CancellationToken.None));
        logger.LogInformation("Translation project publish queued for {ProjectId} by {Email}", id,
            HttpContext.AuthenticatedUserOrThrow().Email);
        return Accepted();
    }

    [HttpGet("{id:long}/syncStatus")]
    public async Task<ActionResult<TranslationSyncStatusDTO>> SyncStatus(long id,
        CancellationToken cancellationToken)
    {
        if (!await database.TranslationProjects.AnyAsync(p => p.Id == id, cancellationToken))
            return NotFound();

        var pending = await database.TranslationValues.AsNoTracking()
            .Where(value => value.ProjectId == id && value.PendingRevision != null)
            .Select(value => value.PendingSince).ToListAsync(cancellationToken);
        var pr = await database.TranslationPullRequests.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProjectId == id, cancellationToken);
        var run = await database.TranslationSyncRuns.AsNoTracking().Where(item => item.ProjectId == id)
            .OrderByDescending(item => item.StartedAt).FirstOrDefaultAsync(cancellationToken);

        return new TranslationSyncStatusDTO
        {
            PendingTranslations = pending.Count,
            OldestPendingAt = pending.Min(),
            PullRequestNumber = pr?.Number,
            PullRequestOpen = pr?.Open ?? false,
            LastRunStatus = run?.Status,
            LastRunError = run?.Error,
        };
    }

    [HttpPost("{id:long}/languages")]
    public async Task<IActionResult> AddLanguage(long id, [FromBody] TranslationLanguageConfigurationRequest request)
    {
        var project = await database.TranslationProjects.FindAsync(id);
        if (project == null)
            return NotFound();

        if (project.Format != TranslationFormat.Manual)
            return BadRequest("Only manual projects can have languages added");

        var code = request.LanguageCode.Trim();
        if (code.Length == 0 || code.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            return BadRequest("Invalid language code");

        if (await database.TranslationProjectLanguages.AnyAsync(l => l.ProjectId == id &&
                l.LanguageCode == code))
        {
            return Conflict("Language already exists");
        }

        await database.TranslationProjectLanguages.AddAsync(new TranslationProjectLanguage
        {
            ProjectId = id, Project = project, LanguageCode = code, DisplayName = request.DisplayName.Trim(),
        });
        var units = await database.TranslationUnits.AsNoTracking().Where(u => u.ProjectId == id && u.Active)
            .ToListAsync();
        foreach (var unit in units)
        {
            await database.TranslationValues.AddAsync(new TranslationValue
            {
                ProjectId = id, Key = unit.Key, LanguageCode = code,
                Text = code == project.SourceLanguage ? unit.SourceText : string.Empty,
                LastImportedText = code == project.SourceLanguage ? unit.SourceText : string.Empty,
                State = code == project.SourceLanguage ?
                    TranslationState.Translated :
                    TranslationState.NeedsTranslation,
                SourceRevisionTranslatedFrom = unit.SourceRevision,
                SourceTextTranslatedFrom = unit.SourceText,
            });
        }

        if (project.DictionaryId is { } dictionaryId)
        {
            var terms = await database.DictionaryTerms.AsNoTracking()
                .Where(term => term.DictionaryId == dictionaryId).ToListAsync();
            foreach (var term in terms)
            {
                await database.DictionaryTermTranslations.AddAsync(new DictionaryTermTranslation
                {
                    DictionaryId = dictionaryId, Word = term.Word, Context = term.Context,
                    LanguageCode = code, State = TranslationState.NeedsTranslation,
                });
            }
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Language {LanguageCode} added to translation project {ProjectId} by {Email}", code,
            id, HttpContext.AuthenticatedUserOrThrow().Email);
        return NoContent();
    }

    [HttpGet("{id:long}/conflicts")]
    public async Task<ActionResult<TranslationImportConflictDTO[]>> Conflicts(long id,
        CancellationToken cancellationToken)
    {
        return await database.TranslationImportConflicts.AsNoTracking()
            .Where(conflict => conflict.ProjectId == id && conflict.ResolvedAt == null)
            .OrderBy(conflict => conflict.CreatedAt)
            .Select(conflict => new TranslationImportConflictDTO
            {
                Id = conflict.Id,
                Key = conflict.Key,
                LanguageCode = conflict.LanguageCode,
                LocalText = conflict.LocalText,
                IncomingText = conflict.IncomingText,
                CreatedAt = conflict.CreatedAt,
            })
            .ToArrayAsync(cancellationToken);
    }

    // TODO: it would be probably good to have a separate conflicts view so that we can grant normal users
    // translation conflict resolve privileges
    [HttpPost("{id:long}/conflicts/{conflictId:long}/resolve")]
    public async Task<IActionResult> ResolveConflict(long id, long conflictId, [FromQuery] bool keepLocal)
    {
        var conflict = await database.TranslationImportConflicts.FindAsync(conflictId);
        if (conflict == null || conflict.ProjectId != id)
            return NotFound();

        if (conflict.ResolvedAt != null)
            return BadRequest("Conflict already resolved");

        var value = await database.TranslationValues.FindAsync(id, conflict.Key, conflict.LanguageCode);
        var project = await database.TranslationProjects.FindAsync(id);
        if (value == null || project == null)
            return NotFound();

        if (keepLocal)
        {
            value.LastImportedText = conflict.IncomingText;
            value.PendingSince ??= DateTime.UtcNow;
            value.PendingRevision = ++project.CurrentRevision;
        }
        else
        {
            var oldText = value.Text;
            var oldState = value.State;
            var previousEditor = value.LastEditedByUserId;
            value.Text = conflict.IncomingText;
            value.LastImportedText = conflict.IncomingText;
            value.LastEditedByUserId = null;
            value.LastEditedAt = DateTime.UtcNow;
            value.State = string.IsNullOrWhiteSpace(value.Text) ?
                TranslationState.NeedsTranslation :
                TranslationState.Translated;
            value.PendingRevision = null;
            value.PendingSince = null;
            value.Version = Guid.NewGuid();
            await database.TranslationChanges.AddAsync(new TranslationChange
            {
                ProjectId = id, Key = conflict.Key, LanguageCode = conflict.LanguageCode,
                Kind = TranslationChangeKind.SystemImport, OldText = oldText, NewText = value.Text,
                OldState = oldState, NewState = value.State,
                ProjectRevision = ++project.CurrentRevision,
            });

            if (previousEditor != null && oldText != value.Text)
            {
                await database.UserNotifications.AddAsync(new UserNotification
                {
                    UserId = previousEditor.Value,
                    DeduplicationKey = $"translation-conflict:{conflictId}:accepted",
                    Type = UserNotificationType.TranslationChangedInRepository,
                    Message = $"Your {conflict.LanguageCode} translation for {conflict.Key} changed in Git.",
                    TargetPath = $"/translations/{project.Slug}/{conflict.LanguageCode}/edit" +
                        $"?key={Uri.EscapeDataString(conflict.Key)}",
                });
            }
        }

        conflict.ResolvedAt = DateTime.UtcNow;
        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction(
            $"Translation conflict {conflictId} resolved ({(keepLocal ? "keep local" : "accept remote")})")
        {
            PerformedById = user.Id,
        });
        await database.SaveChangesAsync();
        logger.LogInformation("Translation conflict {ConflictId} resolved for project {ProjectId} by {Email}; " +
            "kept local: {KeepLocal}", conflictId, id, user.Email, keepLocal);
        return NoContent();
    }

    private static void Apply(TranslationProject project, TranslationProjectConfigurationDTO request,
        TranslationFormat format)
    {
        project.Slug = request.Slug;
        project.Name = request.Name;
        project.SourceLanguage = request.SourceLanguage;
        project.Format = format;

        // Special logic that doesn't allow incompatible parameters to be set based on the type (this is why we don't
        // use the default apply helpers)
        project.RepositoryFullName = format == TranslationFormat.Manual ? null : request.RepositoryFullName;
        project.SourceBranch = format == TranslationFormat.Manual ? null : request.SourceBranch;
        project.SourceFilePattern = format == TranslationFormat.Manual ? null : request.SourceFilePath;
        project.TranslationFilePattern = format == TranslationFormat.Manual ? null : request.TranslationFilePattern;
        project.DictionaryId = request.DictionaryId;
        project.Enabled = request.Enabled;
    }

    private static bool TryValidate(TranslationProjectConfigurationDTO request, out TranslationFormat format,
        out string? error)
    {
        format = request.Format;
        error = null;
        if (!Enum.IsDefined(format))
        {
            error = "Invalid translation format";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Slug) || string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.SourceLanguage) ||
            request.SourceLanguage.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
        {
            error = "Slug, name, and source language are required";
            return false;
        }

        if (format == TranslationFormat.Manual)
            return true;

        if (!ValidRepo(request.RepositoryFullName) || !ValidBranch(request.SourceBranch) ||
            !ValidPath(request.SourceFilePath) || !ValidPath(request.TranslationFilePattern) ||
            request.TranslationFilePattern.Count(c => c == '{') != 1 ||
            !request.TranslationFilePattern.Contains("{language}", StringComparison.Ordinal) ||
            request.SourceFilePath.Contains("{language}", StringComparison.Ordinal))
        {
            error = "Invalid public GitHub repository, branch, or translation file paths";
            return false;
        }

        return true;
    }

    private static bool ValidRepo(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0 &&
            part.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'));
    }

    private static bool ValidBranch(string value)
    {
        return value.Length > 0 && !value.StartsWith("-", StringComparison.Ordinal) &&
            !value.Contains("..", StringComparison.Ordinal) &&
            value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '/' or '.');
    }

    private static bool ValidPath(string value)
    {
        return value.Length > 0 && !value.StartsWith("/", StringComparison.Ordinal) &&
            !value.Contains("..", StringComparison.Ordinal) && !value.Contains('\\') &&
            !value.Contains('*') && !value.Contains('?');
    }
}
