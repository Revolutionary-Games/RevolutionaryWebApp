using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using RecursiveDataAnnotationsValidation;
using Shared;
using Shared.Models;
using Utilities;

/// <summary>
///   Allows modification of the launcher info returned by <see cref="LauncherInfoController"/>
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class LauncherInfoConfigurationController : Controller
{
    private readonly ILogger<LauncherInfoConfigurationController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IConfiguration configuration;

    public LauncherInfoConfigurationController(ILogger<LauncherInfoConfigurationController> logger,
        NotificationsEnabledDb database, IConfiguration configuration)
    {
        this.logger = logger;
        this.database = database;
        this.configuration = configuration;
    }

    [HttpGet]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> Get()
    {
        var currentData = await LauncherInfoController.GenerateLauncherInfoObject(database, configuration);

        var validationResult = new List<ValidationResult>();
        var validator = new RecursiveDataAnnotationValidator();

        if (!validator.TryValidateObjectRecursive(currentData, new ValidationContext(currentData), validationResult))
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Value = $"Validation failed: {string.Join(", ", validationResult.Select(r => r.ToString()))}",
            };
        }

        string? validationError = null;

        // Extra validations that would be hard to make as validation attributes
        if (currentData.LatestVersionOrNull() == null)
        {
            validationError = "No latest version set";
        }

        if (validationError != null)
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Value = $"Extra validation error: {validationError}",
            };
        }

        // Everything is configured right
        return Ok();
    }

    [HttpGet("mirrors")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<LauncherDownloadMirrorDTO>> GetMirrors([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<LauncherDownloadMirror> query;

        try
        {
            query = database.LauncherDownloadMirrors.AsNoTracking().OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("mirrors/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<LauncherDownloadMirrorDTO>> GetMirror(long id)
    {
        var mirror = await database.LauncherDownloadMirrors.FindAsync(id);

        if (mirror == null)
            return NotFound();

        return mirror.GetDTO();
    }

    [HttpDelete("mirrors/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> DeleteMirror(long id)
    {
        var mirror = await database.LauncherDownloadMirrors.FindAsync(id);

        if (mirror == null)
            return NotFound();

        // Disallow delete if currently in use, the DB should restrict a delete like this, but we check anyway
        // to give a better error message
        if (await database.LauncherVersionDownloads.AnyAsync(d => d.MirrorId == mirror.Id))
            return BadRequest("Mirror is in use by a launcher download");

        if (await database.LauncherThriveVersionDownloads.AnyAsync(d => d.MirrorId == mirror.Id))
            return BadRequest("Mirror is in use by a Thrive version download");

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Download mirror \"{mirror.InternalName}\" ({mirror.Id}) deleted",
            PerformedById = user.Id,
        });

        database.LauncherDownloadMirrors.Remove(mirror);

        await database.SaveChangesAsync();

        logger.LogInformation("Download mirror {Id} deleted by {Email}", mirror.Id, user.Email);
        return Ok();
    }

    [HttpPost("mirrors")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> CreateMirror([Required] [FromBody] LauncherDownloadMirrorDTO request)
    {
        var mirror = new LauncherDownloadMirror(request.InternalName, new Uri(request.InfoLink), request.ReadableName)
        {
            BannerImageUrl = request.BannerImageUrl == null ? null : new Uri(request.BannerImageUrl),
            ExtraDescription = request.ExtraDescription,
        };

        if (await database.LauncherDownloadMirrors.FirstOrDefaultAsync(m => m.InternalName == mirror.InternalName) !=
            null)
        {
            return BadRequest("InternalName is already in use");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New download mirror \"{mirror.ReadableName}\" ({mirror.InternalName}) created",
            PerformedById = user.Id,
        });

        await database.LauncherDownloadMirrors.AddAsync(mirror);

        await database.SaveChangesAsync();

        logger.LogInformation("New download mirror {InternalName} ({Id}) created by {Email}", mirror.Id,
            mirror.InternalName, user.Email);
        return Ok();
    }
}
