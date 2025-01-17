namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Forms;
using DevCenterCommunication.Models;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class CrashReportController : Controller
{
    private readonly ILogger<CrashReportController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly ILocalTempFileLocks localTempFileLocks;
    private readonly IBackgroundJobClient jobClient;
    private readonly DiscordNotifications discordNotifications;
    private readonly bool uploadEnabled;
    private readonly Uri baseUrl;

    public CrashReportController(ILogger<CrashReportController> logger,
        NotificationsEnabledDb database, IConfiguration configuration, ILocalTempFileLocks localTempFileLocks,
        IBackgroundJobClient jobClient, DiscordNotifications discordNotifications)
    {
        this.logger = logger;
        this.database = database;
        this.localTempFileLocks = localTempFileLocks;
        this.jobClient = jobClient;
        this.discordNotifications = discordNotifications;

        uploadEnabled = Convert.ToBoolean(configuration["Crashes:Enabled"]);
        baseUrl = configuration.GetBaseUrl();
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CrashReportInfo>>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize, bool searchDuplicates = true, bool searchClosed = true,
        string? searchText = null)
    {
        IQueryable<CrashReport> query;

        // Developers can view all items, others can only view public items
        if (HttpContext.HasAuthenticatedUserWithGroup(GroupType.Developer, AuthenticationScopeRestriction.None))
        {
            // Exclude the output from fetch in DB which might be very large
            query = WithoutLogs(database.CrashReports);
        }
        else
        {
            query = WithoutLogs(database.CrashReports).Where(r => r.Public == true);
        }

        if (!searchDuplicates && !searchClosed)
        {
            query = query.Where(r => r.State == ReportState.Open);
        }
        else if (!searchDuplicates)
        {
            query = query.Where(r => r.State != ReportState.Duplicate);
        }
        else if (!searchClosed)
        {
            query = query.Where(r => r.State != ReportState.Closed);
        }

        if (searchText != null)
        {
            if (searchText.Length < AppInfo.MinimumReportTextSearchLength)
            {
                return BadRequest(
                    $"Search text needs to be at least {AppInfo.MinimumReportTextSearchLength} characters");
            }

            // TODO: should logs be searched as well? Might be a bit performance intensive...
            query = query.Where(r => (r.Description != null && r.Description.Contains(searchText)) ||
                (r.PrimaryCallstack != null && r.PrimaryCallstack.Contains(searchText)) ||
                r.ExitCodeOrSignal.Contains(searchText));
        }

        try
        {
            query = query.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CrashReportDTO>> GetSingle([Required] long id)
    {
        var report = await WithoutLogs(database.CrashReports).Where(r => r.Id == id)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound("Not found or report is private");

        if (!report.Public)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(GroupType.Developer, AuthenticationScopeRestriction.None))
            {
                return NotFound("Not found or report is private");
            }
        }

        return report.GetDTO(HttpContext.HasAuthenticatedUserWithGroup(GroupType.Developer,
            AuthenticationScopeRestriction.None));
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    [HttpGet("{id:long}/logs")]
    public async Task<ActionResult<string>> GetLogs([Required] long id)
    {
        var report = await database.CrashReports.Where(r => r.Id == id).Select(r => new
            {
                r.Logs,
            })
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound();

        return report.Logs;
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    [HttpGet("{id:long}/decodedDump")]
    public async Task<ActionResult<string>> GetDecodedCrash([Required] long id)
    {
        var report = await WithoutLogs(database.CrashReports, true).Where(r => r.Id == id)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound();

        return report.WholeCrashDump ?? string.Empty;
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    [HttpPost("{id:long}/reprocess")]
    public async Task<IActionResult> ReProcessCrashDump([Required] long id)
    {
        var report = await WithoutLogs(database.CrashReports, true).Where(r => r.Id == id)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound();

        if (report.DumpLocalFileName == null)
            return BadRequest("Report no longer has a crash dump file");

        await database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"Report {report.Id} crash dump reprocessing requested")
            {
                PerformedById = HttpContext.AuthenticatedUserOrThrow().Id,
            });

        await database.SaveChangesAsync();

        jobClient.Enqueue<StartStackwalkOnReportJob>(x => x.Execute(report.Id, CancellationToken.None));

        return Ok();
    }

    [HttpGet("{id:long}/duplicates")]
    public async Task<ActionResult<List<long>>> DuplicatesOfThisReport([Required] long id)
    {
        bool developer = HttpContext.HasAuthenticatedUserWithGroup(GroupType.Developer,
            AuthenticationScopeRestriction.None);

        var report = await WithoutLogs(database.CrashReports, false).Where(r => r.Id == id)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound();

        if (!report.Public && !developer)
            return NotFound();

        // Developers can view all items, others can only view public items so also limit the duplicates list
        var query = developer ?
            database.CrashReports.Where(r => r.DuplicateOfId == report.Id) :
            database.CrashReports.Where(r => r.Public == true && r.DuplicateOfId == report.Id);

        // A maximum limit is imposed on the number of returned rows to not return a ton of data
        var results = await query.Select(r => new { r.Id }).OrderByDescending(r => r.Id)
            .Take(AppInfo.MaximumDuplicateReports).ToListAsync();

        return results.Select(r => r.Id).ToList();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    [HttpGet("{id:long}/bySameReporter")]
    public async Task<ActionResult<List<long>>> ReportsFromSameReporter([Required] long id)
    {
        var report = await WithoutLogs(database.CrashReports, true).Where(r => r.Id == id)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound();

        // Developers can view all items, others can only view public items so also limit the duplicates list
        var query = !string.IsNullOrEmpty(report.ReporterEmail) ?
            database.CrashReports.Where(r =>
                r.ReporterEmail == report.ReporterEmail || r.UploadedFrom == report.UploadedFrom) :
            database.CrashReports.Where(r => r.UploadedFrom == report.UploadedFrom);

        // A maximum limit is imposed on the number of returned rows to not return a ton of data
        var results = await query.Select(r => new { r.Id }).OrderBy(r => r.Id - report.Id)
            .Take(AppInfo.MaximumSameReporterReports).ToListAsync();

        // All reports will be by the same reporter as themselves, so filter that out
        return results.Where(r => r.Id != report.Id).Select(r => r.Id).ToList();
    }

    // For now just admin access to the email
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet("{id:long}/reporterEmail")]
    public async Task<ActionResult<string>> GetReporterEmail([Required] long id)
    {
        var report = await WithoutLogs(database.CrashReports).Where(r => r.Id == id)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound();

        return report.ReporterEmail ?? "No reporter email known";
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpDelete("{id:long}/reporterEmail")]
    public async Task<IActionResult> ClearReporterEmail([Required] long id)
    {
        var report = await database.CrashReports.FindAsync(id);

        if (report == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (report.ReporterEmail == null)
            return Ok("Reporter email was not set");

        await database.AdminActions.AddAsync(new AdminAction($"Crash report {report.Id} reporter email cleared")
        {
            PerformedById = user.Id,
        });

        report.ReporterEmail = null;
        report.BumpUpdatedAt();

        await database.SaveChangesWithConflictResolvingAsync(conflictEntries =>
        {
            DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflictEntries, report);
            report.ReporterEmail = null;
            report.BumpUpdatedAt();
        }, CancellationToken.None);

        logger.LogInformation("Crash report {Id} reporter email cleared by {Email}", report.Id,
            user.Email);
        return Ok();
    }

    [HttpPut("{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    public async Task<IActionResult> UpdateSingle([Required] long id, [Required] [FromBody] CrashReportDTO request)
    {
        var report = await database.CrashReports.FindAsync(id);

        if (report == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        var (changes, description, fields) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(report, request);

        if (!changes || fields == null)
            return Ok();

        report.BumpUpdatedAt();

        bool editedDescription = fields.Contains(nameof(CrashReport.Description));

        if (editedDescription)
        {
            report.DescriptionLastEdited = DateTime.UtcNow;
            report.DescriptionLastEditedById = user.Id;
        }

        await database.ActionLogEntries.AddAsync(new ActionLogEntry(editedDescription ?
            $"Crash report {report.Id} edited (edit included description)" :
            $"Crash report {report.Id} edited", description)
        {
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Crash report {Id} edited by {Email}, changes: {Description}", report.Id,
            user.Email, description);
        return Ok();
    }

    [HttpDelete("{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> Delete([Required] long id)
    {
        var report = await WithoutLogs(database.CrashReports).Where(r => r.Id == id)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        // And then delete the report itself, there may be a leftover dump delete job for the report but that will
        // only cause a warning
        await database.AdminActions.AddAsync(new AdminAction($"Crash report {report.Id} queued for deletion")
        {
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        jobClient.Enqueue<DeleteCrashReportJob>(x => x.Execute(report.Id, CancellationToken.None));

        logger.LogInformation("Crash report {Id} queued for deletion by {Email}", report.Id, user.Email);
        return Ok("Queued for deletion");
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitCategories.CrashReport)]
    public ActionResult<CreateCrashReportResponse> CreateReport(
        [Required] [FromForm] CreateCrashReportData request, [Required] IFormFile dump)
    {
        return BadRequest(
            "Current versions of Thrive cannot create crash reports as such they are not being accepted " +
            "at the moment");

        /*if (!uploadEnabled)
            return Problem("Crash uploading is not enabled on the server");

        if (dump.Length > AppInfo.MaxCrashDumpUploadSize)
            return BadRequest("Uploaded crash dump file is too big");

        if (dump.Length < 1)
            return BadRequest("Uploaded crash dump file is empty");

        request.LogFiles ??= "No logs provided";

        if (request.LogFiles.Length > AppInfo.MaxCrashLogsLength)
            return BadRequest("Crash related logs are too big");

        ThrivePlatform parsedPlatform;

        try
        {
            parsedPlatform = ParsePlatform(request.Platform);
        }
        catch (ArgumentException)
        {
            return BadRequest($"Unknown platform value: {request.Platform}");
        }

        var crashTime = DateTime.UnixEpoch + TimeSpan.FromSeconds(request.CrashTime);

        var address = HttpContext.Connection.RemoteIpAddress;
        if (address == null)
            return Problem("Remote IP address could not be read");

        var fileName = Guid.NewGuid() + ".dmp";

        var baseFolder =
            localTempFileLocks.GetTempFilePath(CrashReport.CrashReportTempStorageFolderName);

        var filePath = Path.Combine(baseFolder, fileName);

        var report = new CrashReport
        {
            Public = request.Public,
            ExitCodeOrSignal = request.ExitCode,
            Platform = parsedPlatform,
            Logs = request.LogFiles,
            Store = request.Store,
            Version = request.GameVersion,
            HappenedAt = crashTime,
            UploadedFrom = address,
            DumpLocalFileName = fileName,
            ReporterEmail = request.Email,
        };

        if (report.ReporterEmail != null && !report.ReporterEmail.Contains("@"))
        {
            logger.LogWarning("Ignoring provided crash reporter email that seems invalid: {ReporterEmail}",
                report.ReporterEmail);
            report.ReporterEmail = null;
        }

        if (!string.IsNullOrWhiteSpace(request.ExtraDescription))
        {
            // Just one change line here to not cause the text to go into a new markdown paragraph
            report.Description = $"Reporter provided description:\n{request.ExtraDescription}\n\n";
        }

        await database.CrashReports.AddAsync(report);
        var saveTask = database.SaveChangesAsync();

        using (await localTempFileLocks.LockAsync(baseFolder).ConfigureAwait(false))
        {
            Directory.CreateDirectory(baseFolder);

            // TODO: we don't necessarily have to hold the semaphore while in here, but our files are so small
            // it probably won't cause any issue even with multiple crash reports being uploaded in a few seconds
            await using var stream = System.IO.File.Create(filePath);
            await dump.CopyToAsync(stream);
        }

        try
        {
            await saveTask;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to save crash report, attempting to delete the temp file for it {FilePath}",
                filePath);

            // We don't need the path semaphore here as we have randomly generated file name
            System.IO.File.Delete(filePath);
            return Problem("Failed to save the crash report to the database");
        }

        logger.LogInformation("New crash report ({Id}) created, with crash dump at: {FilePath} from: {Address}",
            report.Id, filePath, address);

        await database.LogEntries.AddAsync(new LogEntry(
            $"New crash report {report.Id} created for {report.StoreOrVersion} on platform: {report.Platform}"));

        saveTask = database.SaveChangesAsync();

        try
        {
            jobClient.Enqueue<StartStackwalkOnReportJob>(x => x.Execute(report.Id, CancellationToken.None));
            jobClient.Schedule<DeleteCrashReportDumpJob>(x => x.Execute(report.Id, CancellationToken.None),
                TimeSpan.FromDays(AppInfo.CrashDumpDumpFileRetentionDays));

            discordNotifications.NotifyAboutNewCrashReport(report, baseUrl);

            // TODO: verify the reporter wants to receive email notifications with a confirmation email

            await saveTask;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to save log entry or create jobs for crash report");
        }

        var response = new CreateCrashReportResponse
        {
            CreatedId = report.Id,
            DeleteKey = report.DeleteKey.ToString(),
        };

        return Created($"reports/{response.CreatedId}", response);*/
    }

    [HttpPost("checkDeleteKey")]
    public async Task<ActionResult<CrashReportDTO>> GetDeleteKeyDetails([Required] [FromBody] string key)
    {
        if (!Guid.TryParse(key, out var parsedKey))
            return BadRequest("Invalid key format");

        var report = await WithoutLogs(database.CrashReports.WhereHashed(nameof(CrashReport.DeleteKey), key))
            .ToAsyncEnumerable().FirstOrDefaultAsync(r => r.DeleteKey == parsedKey);

        if (report == null)
            return NotFound("No report found with key");

        return report.GetDTO(false);
    }

    [HttpPost("useDeleteKey")]
    public async Task<IActionResult> UseDeleteKey([Required] [FromBody] string key)
    {
        if (!Guid.TryParse(key, out var parsedKey))
            return BadRequest("Invalid key format");

        var report = await WithoutLogs(database.CrashReports.WhereHashed(nameof(CrashReport.DeleteKey), key))
            .ToAsyncEnumerable().FirstOrDefaultAsync(r => r.DeleteKey == parsedKey);

        if (report == null)
            return NotFound("No report found with key");

        await database.LogEntries.AddAsync(
            new LogEntry($"Crash report {report.Id} queued for deletion through the use of the delete key"));

        await database.SaveChangesAsync();

        jobClient.Enqueue<DeleteCrashReportJob>(x => x.Execute(report.Id, CancellationToken.None));

        logger.LogInformation("Crash report {Id} queued for deletion with the delete key", report.Id);
        return Ok("Queued for deletion");
    }

    private static ThrivePlatform ParsePlatform(string platform)
    {
        switch (platform.ToLowerInvariant())
        {
            case "win32":
            case "win64":
            case "windows":
                return ThrivePlatform.Windows;
            case "mac":
            case "darwin":
            case "osx":
                return ThrivePlatform.Mac;
            case "linux":
            case "unix":
            case "freebsd":
            case "openbsd":
            case "sunos":
                return ThrivePlatform.Linux;
            default:
                throw new ArgumentException();
        }
    }

    /// <summary>
    ///   Returns the query without getting the logs, this is to reduce the amount of data retrieved when not
    ///   needed
    /// </summary>
    /// <param name="queryable">The query to adapt</param>
    /// <param name="includeDecodedDump">If true the decoded dump data is included, if false it is not included</param>
    /// <returns>The query but with a select that removes fetching the logs database column</returns>
    private static IQueryable<CrashReport> WithoutLogs(IQueryable<CrashReport> queryable,
        bool includeDecodedDump = false)
    {
        if (!includeDecodedDump)
        {
            return queryable.Select(c => new
                CrashReport
                {
                    Id = c.Id,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    HappenedAt = c.HappenedAt,
                    Public = c.Public,
                    ExitCodeOrSignal = c.ExitCodeOrSignal,
                    State = c.State,
                    Platform = c.Platform,
                    Store = c.Store,
                    Version = c.Version,
                    DuplicateOfId = c.DuplicateOfId,
                    DumpLocalFileName = c.DumpLocalFileName,
                    PrimaryCallstack = c.PrimaryCallstack,
                    CondensedCallstack = c.CondensedCallstack,
                    Description = c.Description,
                    DescriptionLastEdited = c.DescriptionLastEdited,
                    DescriptionLastEditedById = c.DescriptionLastEditedById,
                    DeleteKey = c.DeleteKey,
                    ReporterEmail = c.ReporterEmail,
                    UploadedFrom = c.UploadedFrom,
                });
        }

        return queryable.Select(c => new
            CrashReport
            {
                Id = c.Id,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                HappenedAt = c.HappenedAt,
                Public = c.Public,
                ExitCodeOrSignal = c.ExitCodeOrSignal,
                State = c.State,
                Platform = c.Platform,
                Store = c.Store,
                Version = c.Version,
                WholeCrashDump = c.WholeCrashDump,
                DuplicateOfId = c.DuplicateOfId,
                DumpLocalFileName = c.DumpLocalFileName,
                PrimaryCallstack = c.PrimaryCallstack,
                CondensedCallstack = c.CondensedCallstack,
                Description = c.Description,
                DescriptionLastEdited = c.DescriptionLastEdited,
                DescriptionLastEditedById = c.DescriptionLastEditedById,
                DeleteKey = c.DeleteKey,
                ReporterEmail = c.ReporterEmail,
                UploadedFrom = c.UploadedFrom,
            });
    }
}
