namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class LogsController : Controller
{
    private readonly ILogger<LogsController> logger;
    private readonly ApplicationDbContext database;

    public LogsController(ILogger<LogsController> logger,
        ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet("logs")]
    public async Task<PagedResult<LogEntryDTO>> GetLogs([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 1000)] int pageSize)
    {
        IQueryable<LogEntry> query;

        try
        {
            query = database.LogEntries.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet("adminActions")]
    public async Task<PagedResult<AdminActionDTO>> GetAdminActions([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 1000)] int pageSize)
    {
        IQueryable<AdminAction> query;

        try
        {
            query = database.AdminActions.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet("actions")]
    public async Task<PagedResult<ActionLogEntryDTO>> GetActions([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 1000)] int pageSize)
    {
        IQueryable<ActionLogEntry> query;

        try
        {
            query = database.ActionLogEntries.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }
}
