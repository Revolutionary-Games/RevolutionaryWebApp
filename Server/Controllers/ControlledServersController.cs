namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.EC2.Model;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class ControlledServersController : Controller
{
    private readonly ILogger<ControlledServersController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IEC2Controller ec2Controller;
    private readonly IBackgroundJobClient jobClient;

    public ControlledServersController(ILogger<ControlledServersController> logger,
        NotificationsEnabledDb database, IEC2Controller ec2Controller, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.ec2Controller = ec2Controller;
        this.jobClient = jobClient;
    }

    [HttpGet]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<PagedResult<ControlledServerDTO>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<ControlledServer> query;

        try
        {
            query = database.ControlledServers.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<ControlledServerDTO>> GetSingle(long id)
    {
        var server = await database.ControlledServers.FindAsync(id);

        if (server == null)
            return NotFound();

        return server.GetDTO();
    }

    [HttpPost("{id:long}/stop")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<ControlledServerDTO>> ForceStopServer(long id)
    {
        FailIfNotConfigured();

        var server = await database.ControlledServers.FindAsync(id);

        if (server == null)
            return NotFound();

        if (server.Status is ServerStatus.Stopped or ServerStatus.Stopping)
            return Ok("Server already stopped or stopping");

        if (server.Status == ServerStatus.Terminated)
            return BadRequest("Can't stop a terminated server");

        if (string.IsNullOrEmpty(server.InstanceId))
            return BadRequest("Can't stop a server with no instance id");

        var user = HttpContext.AuthenticatedUser()!;

        if (server.ReservationType != ServerReservationType.None)
        {
            await database.AdminActions.AddAsync(new AdminAction
            {
                Message = $"Server {id} force stopped by an admin while it was reserved",
                PerformedById = user.Id,
            });
        }
        else if (server.Status == ServerStatus.Provisioning)
        {
            await database.AdminActions.AddAsync(new AdminAction
            {
                Message = $"Server {id} force stopped by an admin while it was provisioning",
                PerformedById = user.Id,
            });
        }
        else
        {
            await database.AdminActions.AddAsync(new AdminAction
            {
                Message = $"Server {id} force stopped by an admin",
                PerformedById = user.Id,
            });
        }

        await ec2Controller.StopInstance(server.InstanceId, false);
        server.Status = ServerStatus.Stopping;
        UpdateCommonServerStatuses(server);

        await database.SaveChangesAsync();

        jobClient.Schedule<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None),
            TimeSpan.FromSeconds(30));

        return server.GetDTO();
    }

    [HttpPost("{id:long}/terminate")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<ControlledServerDTO>> ForceTerminateServer(long id)
    {
        FailIfNotConfigured();

        var server = await database.ControlledServers.FindAsync(id);

        if (server == null)
            return NotFound();

        if (server.Status == ServerStatus.Terminated)
            return Ok("Server already terminated");

        if (string.IsNullOrEmpty(server.InstanceId))
            return BadRequest("Can't terminate a server with no instance id");

        var user = HttpContext.AuthenticatedUser()!;

        if (server.ReservationType != ServerReservationType.None)
        {
            await database.AdminActions.AddAsync(new AdminAction
            {
                Message = $"Server {id} terminated by an admin while it was reserved",
                PerformedById = user.Id,
            });
        }
        else if (server.Status == ServerStatus.Provisioning)
        {
            await database.AdminActions.AddAsync(new AdminAction
            {
                Message = $"Server {id} terminated by an admin while it was provisioning",
                PerformedById = user.Id,
            });
        }
        else
        {
            await database.AdminActions.AddAsync(new AdminAction
            {
                Message = $"Server {id} terminated by an admin",
                PerformedById = user.Id,
            });
        }

        await ec2Controller.TerminateInstance(server.InstanceId);
        server.Status = ServerStatus.Terminated;
        UpdateCommonServerStatuses(server);

        await database.SaveChangesAsync();

        return server.GetDTO();
    }

    [HttpPost("{id:long}/start")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<ControlledServerDTO>> ForceStartServer(long id)
    {
        FailIfNotConfigured();

        var server = await database.ControlledServers.FindAsync(id);

        if (server == null)
            return NotFound();

        if (server.Status != ServerStatus.Stopped && server.Status != ServerStatus.Terminated)
            return BadRequest("Only a stopped or a terminated server can be started");

        if (string.IsNullOrEmpty(server.InstanceId))
            return BadRequest("Can't start a server with no instance id");

        if (server.Status == ServerStatus.Terminated)
        {
            // Need to re-provision this server

            // This shouldn't create multiple at once, but the API returns a list
            var awsServers = await ec2Controller.LaunchNewInstance();
            bool first = true;

            foreach (var awsServer in awsServers)
            {
                if (!first)
                {
                    logger.LogError(
                        "AWS API created more servers than we wanted, attempting to terminate the extra");
                    await ec2Controller.TerminateInstance(awsServer);
                    throw new Exception("AWS API created more servers than we wanted");
                }

                first = false;

                server.SetProvisioningStatus(awsServer);

                await database.SaveChangesAsync();

                logger.LogInformation("Starting re-provisioning on {Id} from admin control API", server.Id);

                jobClient.Enqueue<ProvisionControlledServerJob>(x =>
                    x.Execute(server.Id, CancellationToken.None));
            }
        }
        else
        {
            // Normal startup is fine
            await ec2Controller.ResumeInstance(server.InstanceId);

            server.Status = ServerStatus.WaitingForStartup;
            server.StatusLastChecked = DateTime.UtcNow;
            server.BumpUpdatedAt();

            await database.SaveChangesAsync();
        }

        jobClient.Schedule<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None),
            TimeSpan.FromSeconds(65));

        return server.GetDTO();
    }

    [HttpPost("{id:long}/queueCleanUp")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> QueueCleanUp(long id)
    {
        FailIfNotConfigured();

        var server = await database.ControlledServers.FindAsync(id);

        if (server == null)
            return NotFound();

        if (server.CleanUpQueued)
            return Ok("Server already has clean up queued");

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Server {id} is queued for clean up",
            PerformedById = HttpContext.AuthenticatedUser()!.Id,
        });

        server.CleanUpQueued = true;
        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id:long}/refreshStatus")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<ControlledServerDTO>> RefreshServerStatus(long id)
    {
        FailIfNotConfigured();

        var server = await database.ControlledServers.FindAsync(id);

        if (server == null)
            return NotFound();

        if (string.IsNullOrEmpty(server.InstanceId))
            return BadRequest("Can't refresh status of a server with no instance id");

        List<Instance> newStatuses;
        try
        {
            newStatuses = await ec2Controller.GetInstanceStatuses(new List<string> { server.InstanceId },
                CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogInformation("Failed to fetch server status due to: {@E}", e);
            return BadRequest("Could not get server status, probably because it is terminated");
        }

        foreach (var status in newStatuses)
        {
            if (status.InstanceId != server.InstanceId)
                continue;

            var newStatus = EC2Controller.InstanceStateToStatus(status);

            if (newStatus != server.Status)
            {
                server.Status = newStatus;
                logger.LogInformation("Server {Id} status is now {Status} after status re-check API request",
                    server.Id, server.Status);

                // TODO: this needs to do reservation status change performing if this is now stopped etc.
                server.StatusLastChecked = DateTime.UtcNow;
                server.BumpUpdatedAt();

                await database.SaveChangesAsync();
            }

            return server.GetDTO();
        }

        return BadRequest("Could not query the server status");
    }

    [HttpPost("refreshStatuses")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<ControlledServerDTO>> RefreshAllStatuses()
    {
        FailIfNotConfigured();

        var servers = await database.ControlledServers.Where(s => s.Status != ServerStatus.Terminated)
            .ToListAsync();

        if (servers.Count < 1)
            return Ok("No servers exist");

        logger.LogInformation("All server statuses refreshed by: {Email}", HttpContext.AuthenticatedUser()!.Email);

        if (servers.Any(s => string.IsNullOrEmpty(s.InstanceId)))
            logger.LogWarning("There are non-terminated controlled servers that don't have an instance id");

        List<Instance> newStatuses;
        try
        {
            newStatuses =
                await ec2Controller.GetInstanceStatuses(
                    servers.Select(s => s.InstanceId).Where(i => !string.IsNullOrEmpty(i)).ToList()!,
                    CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogInformation("Failed to fetch server statuses due to: {@E}", e);
            return BadRequest("Could not get server statuses, probably some instance id is valid for some reason");
        }

        foreach (var status in newStatuses)
        {
            var targetServer = servers.FirstOrDefault(s => s.InstanceId == status.InstanceId);

            if (targetServer == null)
            {
                logger.LogError("Got status response for a server we didn't ask about: {InstanceId}",
                    status.InstanceId);
                continue;
            }

            var newStatus = EC2Controller.InstanceStateToStatus(status);

            if (newStatus == targetServer.Status)
                continue;

            targetServer.Status = newStatus;
            logger.LogInformation("Server {Id} status is now {Status} after status re-check API request",
                targetServer.Id, targetServer.Status);

            targetServer.StatusLastChecked = DateTime.UtcNow;
            targetServer.BumpUpdatedAt();
        }

        await database.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("removeTerminated")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> RemoveTerminated()
    {
        var servers = await database.ControlledServers.Where(s => s.Status == ServerStatus.Terminated)
            .ToListAsync();

        if (servers.Count < 1)
            return Ok("No terminated servers exist");

        var user = HttpContext.AuthenticatedUser()!;
        logger.LogInformation("All terminated servers removed by: {Email}", user.Email);

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = "Terminated servers removed",
            PerformedById = user.Id,
        });

        database.ControlledServers.RemoveRange(servers);
        await database.SaveChangesAsync();
        return Ok();
    }

    [NonAction]
    private static void UpdateCommonServerStatuses(ControlledServer server)
    {
        var now = DateTime.UtcNow;

        server.StatusLastChecked = now;
        server.BumpUpdatedAt();
        server.ReservationType = ServerReservationType.None;
        server.CleanUpQueued = false;

        if (server.RunningSince != null)
            server.TotalRuntime += (now - server.RunningSince.Value).TotalSeconds;
        server.RunningSince = null;
    }

    [NonAction]
    private void FailIfNotConfigured()
    {
        if (!ec2Controller.Configured)
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = "EC2 control not configured",
            };
        }
    }
}
