namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class StackwalkToolController : Controller
{
    private readonly ILogger<StackwalkToolController> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;
    private readonly ILocalTempFileLocks localTempFileLocks;

    private readonly bool enabled;

    public StackwalkToolController(ILogger<StackwalkToolController> logger, ApplicationDbContext database,
        IConfiguration configuration, IBackgroundJobClient jobClient, ILocalTempFileLocks localTempFileLocks)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
        this.localTempFileLocks = localTempFileLocks;

        enabled = !string.IsNullOrEmpty(Convert.ToString(configuration["Crashes:StackwalkService"]));
    }

    [HttpGet]
    public ActionResult<bool> GetEnabled()
    {
        return enabled;
    }

    [HttpPost("submit")]
    [EnableRateLimiting(RateLimitCategories.Stackwalk)]
    public async Task<ActionResult<Guid>> Submit([Required] [FromForm] IFormFile file)
    {
        if (!enabled)
            return BadRequest("This tool is not enabled on the server");

        if (file.Length > AppInfo.MaxCrashDumpUploadSize)
            return BadRequest("Uploaded crash dump is too large");

        if (file.Length < 1)
            return BadRequest("Uploaded crash dump file is empty");

        var address = HttpContext.Connection.RemoteIpAddress;
        if (address == null)
            return Problem("Remote IP address could not be read");

        logger.LogInformation("Starting stackwalk tool run for a request from: {Address}", address);

        var baseFolder =
            localTempFileLocks.GetTempFilePath(StackwalkTask.CrashDumpToolTempStorageFolderName);

        var task = new StackwalkTask
        {
            DumpTempCategory = StackwalkTask.CrashDumpToolTempStorageFolderName,
            DumpFileName = Guid.NewGuid() + ".dmp",
            DeleteDumpAfterRunning = true,

            // TODO: allow the user to specify this
            // https://github.com/Revolutionary-Games/RevolutionaryWebApp/issues/247
            StackwalkPlatform = ThrivePlatform.Windows,
        };
        await database.StackwalkTasks.AddAsync(task);

        var filePath = Path.Combine(baseFolder, task.DumpFileName);

        using (await localTempFileLocks.LockAsync(baseFolder).ConfigureAwait(false))
        {
            Directory.CreateDirectory(baseFolder);

            // TODO: we don't necessarily have to hold the semaphore while in here, but our files are so small
            // it probably won't cause any issue even with multiple requests being uploaded in a few seconds
            await using var stream = System.IO.File.Create(filePath);
            await file.CopyToAsync(stream);
        }

        try
        {
            await database.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e,
                "Failed to save crash dump tool request, attempting to delete the temp file for it {FilePath}",
                filePath);

            // We don't need the path semaphore here as we have randomly generated file name
            System.IO.File.Delete(filePath);
            return Problem("Failed to write to database");
        }

        jobClient.Enqueue<RunStackwalkTaskJob>(x => x.Execute(task.Id, CancellationToken.None));
        return task.Id;
    }

    [HttpGet("check/{id:guid}")]
    public async Task<ActionResult<string>> CheckStatusAndDeleteIfReady([Required] Guid id)
    {
        var task = await database.StackwalkTasks.WhereHashed(nameof(StackwalkTask.Id), id.ToString())
            .ToAsyncEnumerable().FirstOrDefaultAsync(s => s.Id == id);

        if (task == null)
            return NotFound("Invalid key");

        if (task.FinishedAt == null)
        {
            // Not ready yet
            return NoContent();
        }

        logger.LogInformation("Stackwalk tool task result read, deleting the task: {Id}", task.Id);

        database.StackwalkTasks.Remove(task);
        await database.SaveChangesAsync();

        return task.Succeeded ? task.Result ?? "Error: result not set" : "Stackwalking did not succeed";
    }
}
