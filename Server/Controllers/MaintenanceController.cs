namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Hangfire;
using Jobs.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class MaintenanceController : Controller
{
    private readonly ILogger<MaintenanceController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public MaintenanceController(ILogger<MaintenanceController> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    [HttpGet]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<ExecutedMaintenanceOperationDTO>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<ExecutedMaintenanceOperation> query;

        try
        {
            query = database.ExecutedMaintenanceOperations.AsNoTracking().OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpPost("start")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> StartMaintenanceOperation([FromBody] string operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            return BadRequest("No operation type specified");
        }

        (string Name, string? ExtraDescription, Action<IBackgroundJobClient, long> Start) operation;

        try
        {
            operation = EnumerateMaintenanceOperations().First(t => t.Name == operationName);
        }
        catch (InvalidOperationException)
        {
            return BadRequest("Bad operation type specified");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Maintenance operation {operation.Name} started",
            PerformedById = user.Id,
        });

        var operationData = new ExecutedMaintenanceOperation(operation.Name)
        {
            PerformedById = user.Id,
        };

        await database.ExecutedMaintenanceOperations.AddAsync(operationData);

        await database.SaveChangesAsync();

        logger.LogInformation("Started maintenance operation {Name}, by {Email}, operation id: {Id}", operation.Name,
            user.Email, operationData.Id);

        try
        {
            operation.Start(jobClient, operationData.Id);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to start maintenance operation");

            operationData.Failed = true;
            operationData.ExtendedDescription = "Failed to queue job for running";

            await database.SaveChangesAsync();

            return Problem("Internal server error starting the operation");
        }

        return Ok();
    }

    [HttpGet("available")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public IEnumerable<Tuple<string, string>> GetAvailableMaintenanceOperations()
    {
        return EnumerateMaintenanceOperations()
            .Select(t => new Tuple<string, string>(t.Name, t.ExtraDescription ?? "No description"));
    }

    [NonAction]
    private static IEnumerable<(string Name, string? ExtraDescription, Action<IBackgroundJobClient, long> Start)>
        EnumerateMaintenanceOperations()
    {
        yield return ("cleanSessions", "Delete all sessions that are older than one day", StartCleanSessions);
    }

    [NonAction]
    private static void StartCleanSessions(IBackgroundJobClient jobClient, long operationId)
    {
        jobClient.Enqueue<ClearAllSlightlyInactiveSessions>(x => x.Execute(operationId, CancellationToken.None));
    }
}
