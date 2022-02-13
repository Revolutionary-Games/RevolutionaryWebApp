using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Models;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class BackupController : Controller
{
    private readonly ILogger<BackupController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly BackupHandler backupHandler;

    public BackupController(ILogger<BackupController> logger, NotificationsEnabledDb database,
        BackupHandler backupHandler)
    {
        this.logger = logger;
        this.database = database;
        this.backupHandler = backupHandler;
    }

    [HttpGet]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<BackupDTO>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<Backup> query;

        try
        {
            query = database.Backups.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("status")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public ActionResult<bool> GetStatus()
    {
        return IsConfigured();
    }

    [HttpPost("{id:long}/download")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<string>> GetDownloadLink([Required] long id)
    {
        if (!IsConfigured())
            return Problem("Backups not configured on the server");

        var backup = await database.Backups.FindAsync(id);

        if (backup == null)
            return NotFound();

        if (!backup.Uploaded)
            return BadRequest("The backup has not been uploaded yet, please try again later");

        var user = HttpContext.AuthenticatedUserOrThrow();

        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"Backup {backup.Id} downloaded",
            PerformedById = user.Id,
        });

        logger.LogInformation("Backup {Name} downloaded by {Email}", backup.Name, user.Email);

        await database.SaveChangesAsync();
        return backupHandler.GetDownloadUrlForBackup(backup);
    }

    [HttpDelete("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<string>> Delete([Required] long id)
    {
        if (!IsConfigured())
            return Problem("Backups not configured on the server");

        var backup = await database.Backups.FindAsync(id);

        if (backup == null)
            return NotFound();

        if (!backup.Uploaded && DateTime.UtcNow - backup.CreatedAt < TimeSpan.FromHours(4))
        {
            return BadRequest(
                "The backup has not been uploaded yet, and it's not over 4 hours old (stuck uploads can be deleted)");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"Backup {backup.Id} downloaded",
            PerformedById = user.Id,
        });
        database.Backups.Remove(backup);

        try
        {
            await backupHandler.DeleteRemoteBackupFile(backup);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to delete remote file for backup {Id}", id);
            return Problem("Failed to delete backup in remote storage");
        }

        logger.LogInformation("Backup {Name} deleted by {Email}", backup.Name, user.Email);

        await database.SaveChangesAsync();

        return Ok();
    }

    [NonAction]
    private bool IsConfigured()
    {
        return backupHandler.Configured;
    }
}
