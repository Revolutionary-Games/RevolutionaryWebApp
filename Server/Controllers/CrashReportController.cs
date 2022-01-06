using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Hangfire;
    using Jobs;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared;
    using Shared.Forms;
    using Shared.Models;
    using Shared.Models.Enums;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class CrashReportController : Controller
    {
        private readonly ILogger<CrashReportController> logger;
        private readonly ApplicationDbContext database;
        private readonly ILocalTempFileLocks localTempFileLocks;
        private readonly IBackgroundJobClient jobClient;
        private readonly DiscordNotifications discordNotifications;
        private readonly bool uploadEnabled;
        private readonly Uri baseUrl;

        public CrashReportController(ILogger<CrashReportController> logger,
            ApplicationDbContext database, IConfiguration configuration, ILocalTempFileLocks localTempFileLocks,
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
        public async Task<PagedResult<CrashReportInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            IQueryable<CrashReport> query;

            try
            {
                // Developers can view all items, others can only view public items
                if (HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Developer,
                        AuthenticationScopeRestriction.None))
                {
                    // Exclude the output from fetch in DB which might be very large
                    query = WithoutLogs(database.CrashReports).OrderBy(sortColumn, sortDirection);
                }
                else
                {
                    query = WithoutLogs(database.CrashReports).Where(p => p.Public == true)
                        .OrderBy(sortColumn, sortDirection);
                }
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
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
                if (!HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Developer,
                        AuthenticationScopeRestriction.None))
                {
                    return NotFound("Not found or report is private");
                }
            }

            return report.GetDTO();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
        [HttpGet("{id:long}/logs")]
        public async Task<ActionResult<string>> GetLogs([Required] long id)
        {
            var report = await database.CrashReports.AsQueryable().Where(r => r.Id == id).Select(
                    r => new
                    {
                        r.Logs,
                    })
                .FirstOrDefaultAsync();

            if (report == null)
                return NotFound();

            return report.Logs ?? string.Empty;
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
        [HttpGet("{id:long}/decodedDump")]
        public async Task<ActionResult<string>> GetDecodedCrash([Required] long id)
        {
            var report = await WithoutLogs(database.CrashReports, true).Where(r => r.Id == id)
                .FirstOrDefaultAsync();

            if (report == null)
                return NotFound();

            return report.WholeCrashDump ?? string.Empty;
        }

        [HttpPost]
        public async Task<ActionResult<CreateCrashReportResponse>> CreateReport(
            [Required] [FromForm] CreateCrashReportData request, [Required] IFormFile dump)
        {
            if (!uploadEnabled)
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

            var semaphore =
                localTempFileLocks.GetTempFilePath(CrashReport.CrashReportTempStorageFolderName, out string baseFolder);

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
                report.Description = $"Reporter provided description:\n{request.ExtraDescription}";
            }

            await database.CrashReports.AddAsync(report);
            var saveTask = database.SaveChangesAsync();

            await semaphore.WaitAsync();
            try
            {
                Directory.CreateDirectory(baseFolder);

                // TODO: we don't necessarily have to hold the semaphore while in here, but our files are so small
                // it probably won't cause any issue even with multiple crash reports being uploaded in a few seconds
                await using var stream = System.IO.File.Create(filePath);
                await dump.CopyToAsync(stream);
            }
            finally
            {
                semaphore.Release();
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

            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message =
                    $"New crash report {report.Id} created for {report.StoreOrVersion} on platform: {report.Platform}",
            });

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

            var response = new CreateCrashReportResponse()
            {
                CreatedId = report.Id,
                DeleteKey = report.DeleteKey.ToString(),
            };

            return Created($"reports/{response.CreatedId}", response);
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
                        State = c.State,
                        Platform = c.Platform,
                        Store = c.Store,
                        Version = c.Version,
                        DuplicateOfId = c.DuplicateOfId,
                        DumpLocalFileName = c.DumpLocalFileName,
                        PrimaryCallstack = c.PrimaryCallstack,
                        Description = c.Description,
                        DescriptionLastEdited = c.DescriptionLastEdited,
                        DescriptionLastEditedById = c.DescriptionLastEditedById,
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
                    State = c.State,
                    Platform = c.Platform,
                    Store = c.Store,
                    Version = c.Version,
                    WholeCrashDump = c.WholeCrashDump,
                    DuplicateOfId = c.DuplicateOfId,
                    DumpLocalFileName = c.DumpLocalFileName,
                    PrimaryCallstack = c.PrimaryCallstack,
                    Description = c.Description,
                    DescriptionLastEdited = c.DescriptionLastEdited,
                    DescriptionLastEditedById = c.DescriptionLastEditedById,
                });
        }
    }
}
