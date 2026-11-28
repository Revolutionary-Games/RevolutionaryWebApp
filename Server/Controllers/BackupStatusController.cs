namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class BackupStatusController : Controller
{
    private readonly NotificationsEnabledDb database;

    public BackupStatusController(NotificationsEnabledDb database)
    {
        this.database = database;
    }

    [HttpGet]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [APIKeyScope(APIKeyScope.Administration)]
    public async Task<ActionResult<PagedResult<BackupRunStatusDTO>>> Get(string sortColumn,
        SortDirection sortDirection, [Range(1, int.MaxValue)] int page, [Range(1, 100)] int pageSize)
    {
        IOrderedQueryable<BackupRunStatus> ordered;
        try
        {
            ordered = (IOrderedQueryable<BackupRunStatus>)database.BackupRunStatuses.AsNoTracking()
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid sort column");
        }

        // This adds additional ID sort to solve ordering on the same timestamps
        var query = sortDirection == SortDirection.Descending ?
            ordered.ThenByDescending(run => run.Id) : ordered.ThenBy(run => run.Id);
        var runs = await query.ToPagedResultAsync(page, pageSize);

        return runs.ConvertResult(run => run.GetDTO());
    }

    [HttpPost]
    [AuthorizeAccessKeyFilter(RequiredAccess = AccessKeyType.BackupStatus)]
    public async Task<IActionResult> Report([FromBody] BackupStatusReport report)
    {
        if (report.FinishedAt == default)
            return BadRequest("A finish timestamp is required");

        await database.BackupRunStatuses.AddAsync(new BackupRunStatus
        {
            Host = report.Host.Trim(),
            Operation = report.Operation,
            FinishedAt = report.FinishedAt.ToUniversalTime(),
            ExitCode = report.ExitCode!.Value,
        });
        await database.SaveChangesAsync();

        return NoContent();
    }
}
