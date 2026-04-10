namespace RevolutionaryWebApp.Server.Controllers.Admin;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using SharedBase.Utilities;
using StackExchange.Redis;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
public class RemoteRunnersController : Controller
{
    private readonly ILogger<RemoteRunnersController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IConnectionMultiplexer multiplexer;

    public RemoteRunnersController(ILogger<RemoteRunnersController> logger, NotificationsEnabledDb database,
        IConnectionMultiplexer multiplexer)
    {
        this.logger = logger;
        this.database = database;
        this.multiplexer = multiplexer;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<RemoteRunnerDTO>>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<RemoteRunner> query;

        try
        {
            // We want jobs to show in the UI
            query = database.RemoteRunners.Include(r => r.ReservedJob).OrderBy(sortColumn, sortDirection);
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
    public async Task<ActionResult<RemoteRunnerDTO>> GetSingle(long id)
    {
        var server = await database.RemoteRunners.FindAsync(id);

        if (server == null)
            return NotFound();

        return server.GetDTO();
    }

    [HttpPost]
    public async Task<IActionResult> Create([Required] [FromBody] RemoteRunnerDTO request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Missing name");

        request.Name = request.Name.Trim();

        // Don't allow duplicate names
        if (await database.RemoteRunners.FirstOrDefaultAsync(r => r.Name.ToLower() == request.Name.ToLower()) != null)
        {
            return BadRequest("There is already a runner with the given name");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        var runner = new RemoteRunner(request.Name)
        {
            Priority = request.Priority,
            Description = request.Description,
            Tags = request.Tags.Trim(),
            AccessId = Guid.NewGuid(),
            SecretKey = Guid.NewGuid(),
        };
        await database.RemoteRunners.AddAsync(runner);

        await database.AdminActions.AddAsync(new AdminAction($"New job runner created ({request.Name})")
        {
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("New job runner created {Id} ({runnerName}) by {Email}", runner.Id, request.Name,
            user.Email);

        return Ok($"Created. Please copy this secret access key now: {runner.SecretKey}");
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        var runner = await database.RemoteRunners.Include(r => r.ReservedJob).FirstOrDefaultAsync(r => r.Id == id);

        if (runner == null)
            return NotFound();

        if (runner.ReservedJob != null)
            return BadRequest("Runners with a reserved job cannot be deleted");

        if (!runner.DisallowJobs)
            return BadRequest("Runner needs to first disallow jobs before it can be deleted");

        var user = HttpContext.AuthenticatedUserOrThrow();

        await database.AdminActions.AddAsync(new AdminAction($"Job runner {id} ({runner.Name.Truncate()}) deleted")
        {
            PerformedById = user.Id,
        });

        database.RemoteRunners.Remove(runner);
        await database.SaveChangesAsync();

        logger.LogInformation("Job runner {Id} ({Name}) deleted by {Email}", runner.Id, runner.Name, user.Email);

        // Notify connections to hopefully close them very soon if any are open
        try
        {
            await RunnerConnectionHandler.NotifyOpenedConnection(runner.Id, -42, multiplexer);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to notify runner connections to close for deleted runner");
        }

        return Ok();
    }

    [HttpPost("{id:long}/offline")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> MarkOffline(long id, [Required] bool offline)
    {
        var server = await database.RemoteRunners.FindAsync(id);

        if (server == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (server.DisallowJobs == offline)
            return Ok("Already in desired state");

        if (offline)
        {
            server.DisallowJobs = true;
            await database.AdminActions.AddAsync(new AdminAction($"Runner {id} marked disallowed for new jobs")
            {
                PerformedById = user.Id,
            });
        }
        else
        {
            server.DisallowJobs = false;

            await database.AdminActions.AddAsync(new AdminAction($"Runner {id} can start new jobs again")
            {
                PerformedById = user.Id,
            });
        }

        server.BumpUpdatedAt();
        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [Required] [FromBody] RemoteRunnerDTO request)
    {
        var runner = await database.RemoteRunners.FindAsync(id);

        if (runner == null)
            return NotFound();

        request.Name = request.Name.Trim();
        request.Tags = request.Tags.Trim();
        if (runner.Name != request.Name)
        {
            if (await database.RemoteRunners.FirstOrDefaultAsync(r => r.Name.ToLower() == request.Name.ToLower()) !=
                null)
            {
                return BadRequest("There is already a runner with the new name");
            }
        }

        var (changed, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(runner, request);

        if (!changed)
            return Ok();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction($"Runner {id} edited", description)
        {
            PerformedById = user.Id,
        });

        runner.BumpUpdatedAt();
        await database.SaveChangesAsync();

        logger.LogInformation("Runner {Id} edited by {Email}, changes: {Description}", id, user.Email, description);

        return Ok();
    }

    /*
    // TODO: reimplement this (we need a new message to tell the client to clean all of its cache and a reply to know
    // when it has done so)

    [HttpPost("{id:long}/queueCleanUp")]
    public async Task<IActionResult> QueueCleanUp(long id)
    {
        var server = await database.RemoteRunners.FindAsync(id);

        if (server == null)
            return NotFound();

        if (server.CleanUpQueued)
            return Ok("Server already has clean up queued");

        await database.AdminActions.AddAsync(new AdminAction($"Server {id} is queued for clean up")
        {
            PerformedById = HttpContext.AuthenticatedUserOrThrow().Id,
        });

        server.CleanUpQueued = true;
        server.BumpUpdatedAt();
        await database.SaveChangesAsync();

        return Ok();
    }*/
}
