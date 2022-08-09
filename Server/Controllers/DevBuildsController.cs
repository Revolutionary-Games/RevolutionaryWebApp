using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Forms;
using Shared.Models;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class DevBuildsController : Controller
{
    private readonly ILogger<DevBuildsController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly DiscordNotifications discordNotifications;

    public DevBuildsController(ILogger<DevBuildsController> logger, NotificationsEnabledDb database,
        DiscordNotifications discordNotifications)
    {
        this.logger = logger;
        this.database = database;
        this.discordNotifications = discordNotifications;
    }

    [HttpGet]
    [ResponseCache(Duration = 300)]
    public async Task<ActionResult<DevBuildsStatisticsDTO>> Get()
    {
        var buildsSize = await database.DevBuilds.Include(b => b.StorageItem).SumAsync(b =>
            b.StorageItem!.Size.HasValue ? Convert.ToInt64(b.StorageItem.Size.Value) : 0L);

        var dehydratedSize = await database.DehydratedObjects.Include(d => d.StorageItem)
            .SumAsync(b => b.StorageItem!.Size.HasValue ? Convert.ToInt64(b.StorageItem.Size.Value) : 0L);

        var totalBuilds = await database.DevBuilds.CountAsync();

        DateTime? botdCreated = null;

        foreach (var build in await database.DevBuilds.Where(b => b.BuildOfTheDay).ToListAsync())
        {
            if (botdCreated == null || build.CreatedAt > botdCreated)
            {
                botdCreated = build.CreatedAt;
            }
        }

        DateTime? latestBuild = null;

        if (totalBuilds > 0)
        {
            latestBuild = await database.DevBuilds.MaxAsync(b => b.CreatedAt);
        }

        var result = new DevBuildsStatisticsDTO
        {
            TotalBuilds = totalBuilds,
            TotalDownloads = await database.DevBuilds.SumAsync(b => b.Downloads),
            DehydratedFiles = await database.DehydratedObjects.CountAsync(),
            ImportantBuilds = await database.DevBuilds.CountAsync(b => b.Important),
            BOTDUpdated = botdCreated,
            LatestBuild = latestBuild,
            DevBuildsSize = buildsSize,
            TotalSpaceUsed = buildsSize + dehydratedSize,
        };

        return result;
    }

    [HttpGet("list")]
    [AuthorizeRoleFilter]
    public async Task<ActionResult<PagedResult<DevBuildDTO>>> GetBuilds([Required] DevBuildSearchType type,
        [Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<DevBuild> query;

        try
        {
            query = database.DevBuilds.Where(b => type == DevBuildSearchType.BOTD ?
                b.BuildOfTheDay :
                (type == DevBuildSearchType.NonAnonymous ? !b.Anonymous : b.Anonymous)
            ).OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException() { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("{id:long}")]
    [AuthorizeRoleFilter]
    public async Task<ActionResult<DevBuildDTO>> GetSingleBuild([Required] long id)
    {
        var build = await database.DevBuilds.FindAsync(id);

        if (build == null)
            return NotFound();

        return build.GetDTO();
    }

    [HttpGet("{id:long}/siblings")]
    [AuthorizeRoleFilter]
    public async Task<ActionResult<List<long>>> GetBuildSiblingIds([Required] long id)
    {
        var build = await database.DevBuilds.FindAsync(id);

        if (build == null)
            return NotFound();

        return await database.DevBuilds.Where(b => b.BuildHash == build.BuildHash && b.Id != build.Id)
            .Select(b => b.Id).ToListAsync();
    }

    [HttpPost("{id:long}/verify")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
    public async Task<IActionResult> VerifyBuild([Required] long id, bool siblingsAsWell = true)
    {
        var build = await database.DevBuilds.FindAsync(id);

        if (build == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        bool didSomething = false;

        if (siblingsAsWell)
        {
            foreach (var sibling in await GetSiblingBuilds(build))
            {
                if (sibling.Verified)
                    continue;

                logger.LogInformation("Marking sibling devbuild {Id} as verified as well", sibling.Id);
                sibling.Verified = true;
                sibling.VerifiedById = user.Id;

                didSomething = true;
            }
        }

        if (!build.Verified)
        {
            logger.LogInformation("Marking devbuild {Id} as verified by {Email}", build.Id, user.Email);
            build.Verified = true;
            build.VerifiedById = user.Id;

            didSomething = true;
        }

        if (!didSomething)
            return Ok("Nothing needed to be marked as verified");

        await database.ActionLogEntries.AddAsync(new ActionLogEntry()
        {
            Message = $"Build {id} marked verified",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id:long}/verify")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
    public async Task<IActionResult> UnVerifyBuild([Required] long id, bool siblingsAsWell = true)
    {
        var build = await database.DevBuilds.FindAsync(id);

        if (build == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        bool didSomething = false;

        if (siblingsAsWell)
        {
            foreach (var sibling in await GetSiblingBuilds(build))
            {
                if (!sibling.Verified)
                    continue;

                logger.LogInformation("Marking sibling devbuild {Id} as unverified as well", sibling.Id);
                sibling.Verified = false;
                sibling.VerifiedById = null;

                didSomething = true;
            }
        }

        if (build.Verified)
        {
            logger.LogInformation("Marking devbuild {Id} unverified by user {Email}", build.Id, user.Email);
            build.Verified = false;
            build.VerifiedById = null;

            didSomething = true;
        }

        if (!didSomething)
            return Ok("Nothing needed to be unverified");

        await database.ActionLogEntries.AddAsync(new ActionLogEntry()
        {
            Message = $"Verification removed from build {id}",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpPut("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
    public async Task<IActionResult> UpdateDevBuild([Required] long id,
        [FromBody] [Required] DevBuildUpdateForm request)
    {
        var build = await database.DevBuilds.FindAsync(id);

        if (build == null)
            return NotFound();

        bool changes = false;

        if (build.Description != request.Description)
        {
            build.Description = request.Description;
            changes = true;
        }

        if (!changes)
            return Ok("No modifications");

        var user = HttpContext.AuthenticatedUser()!;

        await database.ActionLogEntries.AddAsync(new ActionLogEntry()
        {
            Message = $"Build {id} info modified",
            PerformedById = user.Id,
        });

        logger.LogInformation("DevBuild {Id} was modified by {Email}", build.Id, user.Email);

        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("botd")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
    public async Task<IActionResult> SetBuildOfTheDay([FromBody] [Required] long buildId)
    {
        var build = await database.DevBuilds.FindAsync(buildId);

        if (build == null)
            return NotFound();

        if (build.BuildOfTheDay)
            return BadRequest("Build is already BOTD");

        if (build.Anonymous && !build.Verified)
            return BadRequest("Can't make anonymous, non verified build the BOTD");

        if (string.IsNullOrWhiteSpace(build.Description))
            return BadRequest("BOTD build must have a description");

        var user = HttpContext.AuthenticatedUser()!;
        logger.LogInformation("Build {Id} will be set as the BOTD by {Email}", build.Id, user.Email);

        // Unmark all BOTD of the day builds
        await RemoveAllBOTDStatuses();

        foreach (var sibling in await GetSiblingBuilds(build))
        {
            logger.LogInformation("Marking sibling devbuild {Id} as BOTD as well", sibling.Id);

            if (sibling.Verified != build.Verified)
            {
                logger.LogInformation("Setting sibling build verified status the same before BOTD status set");
                sibling.Verified = build.Verified;
                sibling.VerifiedById = build.VerifiedById;
            }

            sibling.BuildOfTheDay = true;
            sibling.Important = true;
            sibling.Keep = true;

            // Always copy description on setting BOTD status
            sibling.Description = build.Description;
        }

        build.BuildOfTheDay = true;
        build.Important = true;
        build.Keep = true;

        await database.ActionLogEntries.AddAsync(new ActionLogEntry()
        {
            Message = $"Build {build.Id} along with siblings is now the BOTD",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        logger.LogInformation("BOTD updated");

        // TODO: limit this to a couple per hour?
        discordNotifications.NotifyAboutNewBOTD(build, user.NameOrEmail);

        return Ok();
    }

    [HttpDelete("botd")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> RemoveBuildOfTheDay()
    {
        // Unmark all BOTD of the day builds
        await RemoveAllBOTDStatuses();

        var user = HttpContext.AuthenticatedUser()!;
        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = "BOTD unset",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        logger.LogInformation("BOTDs cleared by {Email}", user.Email);

        return Ok();
    }

    [NonAction]
    private Task<List<DevBuild>> GetSiblingBuilds(DevBuild devBuild)
    {
        return database.DevBuilds.Where(b =>
            b.BuildHash == devBuild.BuildHash && b.Branch == devBuild.Branch && b.Id != devBuild.Id).ToListAsync();
    }

    private async Task RemoveAllBOTDStatuses()
    {
        foreach (var build in await database.DevBuilds.Where(b => b.BuildOfTheDay).ToListAsync())
        {
            logger.LogInformation("Unmarking build {Id} being the BOTD", build.Id);
            build.BuildOfTheDay = false;
        }
    }
}