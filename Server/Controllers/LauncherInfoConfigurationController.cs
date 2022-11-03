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
using DevCenterCommunication.Models;
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

    [HttpPut("mirrors/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> UpdateMirror([Required] [FromBody] LauncherDownloadMirrorDTO request)
    {
        var mirror = await database.LauncherDownloadMirrors.FindAsync(request.Id);

        if (mirror == null)
            return NotFound();

        PreProcessMirrorRequest(request);

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(mirror, request);

        if (!changes)
            return Ok();

        mirror.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Download Mirror {mirror.Id} edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Download Mirror {Id} edited by {Email}, changes: {Description}", mirror.Id,
            user.Email, description);

        return Ok();
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
        PreProcessMirrorRequest(request);

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

    // Launcher launcher versions

    [HttpGet("launcherVersions")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<LauncherLauncherVersionDTO>> GetLauncherVersions([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<LauncherLauncherVersion> query;

        try
        {
            query = database.LauncherLauncherVersions.AsNoTracking().OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("launcherVersions/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<LauncherLauncherVersionDTO>> GetLauncherVersion(long id)
    {
        var version = await database.LauncherLauncherVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        return version.GetDTO();
    }

    [HttpDelete("launcherVersions/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> DeleteLauncherVersion(long id)
    {
        var version = await database.LauncherLauncherVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        // Cannot delete if currently latest
        if (version.Latest)
            return BadRequest("Cannot delete latest version");

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Launcher version {version.Version} ({version.Id}) deleted",
            PerformedById = user.Id,
        });

        database.LauncherLauncherVersions.Remove(version);

        await database.SaveChangesAsync();

        logger.LogInformation("Launcher version {Id} deleted by {Email}", version.Id, user.Email);
        return Ok();
    }

    [HttpPost("launcherVersions/{id:long}/latest")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> MakeLauncherVersionLatest(long id)
    {
        var version = await database.LauncherLauncherVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        if (version.Latest)
            return Ok();

        var previous = await database.LauncherLauncherVersions.FirstOrDefaultAsync(v => v.Latest == true);

        await database.Database.BeginTransactionAsync();

        if (previous != null)
        {
            logger.LogInformation("Removing latest status from launcher version {Id}", previous.Id);
            previous.Latest = false;
            previous.BumpUpdatedAt();

            // We have to save here due to the unique index on latest
            await database.SaveChangesAsync();
        }

        version.Latest = true;
        version.BumpUpdatedAt();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Launcher version {version.Version} (previous: {previous?.Version}) is now latest",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        await database.Database.CommitTransactionAsync();

        logger.LogInformation("Launcher version {Id} is now set latest by {Email}", version.Id, user.Email);
        return Ok();
    }

    [HttpPost("launcherVersions")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> CreateLauncherVersion([Required] [FromBody] LauncherLauncherVersionDTO request)
    {
        if (!Version.TryParse(request.Version, out _))
            return BadRequest("Given version number doesn't match syntax required for version");

        var version = new LauncherLauncherVersion(request.Version)
        {
            Latest = false,
        };

        if (await database.LauncherLauncherVersions.FirstOrDefaultAsync(v => v.Version == version.Version) !=
            null)
        {
            return BadRequest("Version number is already in use");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New launcher info version {version.Version} created",
            PerformedById = user.Id,
        });

        await database.LauncherLauncherVersions.AddAsync(version);

        await database.SaveChangesAsync();

        logger.LogInformation("New launcher info version {Version} ({Id}) created by {Email}", version.Version,
            version.Id, user.Email);
        return Ok(version.Id.ToString());
    }

    // Launcher Thrive versions

    [HttpGet("thriveVersions")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<LauncherThriveVersionDTO>> GetThriveVersions([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<LauncherThriveVersion> query;

        try
        {
            query = database.LauncherThriveVersions.AsNoTracking().OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("thriveVersions/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<LauncherThriveVersionDTO>> GetThriveVersion(long id)
    {
        var version = await database.LauncherThriveVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        return version.GetDTO();
    }

    [HttpPut("thriveVersions/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> UpdateThriveVersion([Required] [FromBody] LauncherThriveVersionDTO request)
    {
        var version = await database.LauncherThriveVersions.FindAsync(request.Id);

        if (version == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(version, request);

        if (!changes)
            return Ok();

        version.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Thrive version {version.ReleaseNumber} ({version.Id}) edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Thrive version {Id} edited by {Email}, changes: {Description}", version.Id,
            user.Email, description);

        return Ok();
    }

    [HttpDelete("thriveVersions/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> DeleteThriveVersion(long id)
    {
        var version = await database.LauncherThriveVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        if (version.Latest)
            return BadRequest("Version that's marked as the latest can't be deleted");

        if (version.Enabled)
            return BadRequest("Version needs to be disabled before deleting");

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Thrive version {version.ReleaseNumber} ({version.Id}) deleted",
            PerformedById = user.Id,
        });

        database.LauncherThriveVersions.Remove(version);

        await database.SaveChangesAsync();

        logger.LogInformation("Thrive version {Id} deleted by {Email}", version.Id, user.Email);
        return Ok();
    }

    [HttpPost("thriveVersions/{id:long}/latest")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> MakeThriveVersionLatest(long id)
    {
        var version = await database.LauncherThriveVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        if (version.Latest)
            return Ok();

        if (!version.Enabled)
            return BadRequest("Version needs to be enabled before it can be the latest");

        var previous =
            await database.LauncherThriveVersions.FirstOrDefaultAsync(v =>
                v.Latest == true && v.Stable == version.Stable);

        await database.Database.BeginTransactionAsync();

        if (previous != null)
        {
            logger.LogInformation("Removing latest status from Thrive version {Id}", previous.Id);
            previous.Latest = false;
            previous.BumpUpdatedAt();

            // We have to save here due to the unique index on latest
            await database.SaveChangesAsync();
        }

        version.Latest = true;
        version.BumpUpdatedAt();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message =
                $"Thrive version {version.ReleaseNumber} (previous: {previous?.ReleaseNumber}) is now latest for stable: {version.Stable}",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        await database.Database.CommitTransactionAsync();

        logger.LogInformation("Thrive version {Id} is now set latest by {Email}", version.Id, user.Email);
        return Ok();
    }

    [HttpPost("thriveVersions/{id:long}/enable")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> EnableThriveVersion(long id)
    {
        var version = await database.LauncherThriveVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        if (version.Enabled)
            return Ok();

        version.Enabled = true;
        version.BumpUpdatedAt();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Thrive version {version.ReleaseNumber} is now enabled",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Thrive version {Id} is now enabled by {Email}", version.Id, user.Email);
        return Ok();
    }

    [HttpPost("thriveVersions/{id:long}/disable")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> DisableThriveVersion(long id)
    {
        var version = await database.LauncherThriveVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        if (!version.Enabled)
            return Ok();

        version.Enabled = false;
        version.BumpUpdatedAt();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Thrive version {version.ReleaseNumber} is now disabled",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Thrive version {Id} is now disabled by {Email}", version.Id, user.Email);
        return Ok();
    }

    [HttpPost("thriveVersions")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> CreateThriveVersion([Required] [FromBody] LauncherThriveVersionDTO request)
    {
        bool stable = true;

        if (request.ReleaseNumber.Contains("-"))
        {
            var parts = request.ReleaseNumber.Split("-");

            bool valid = parts.Length == 2;

            if (valid)
            {
                if (parts[1] != "beta")
                    valid = false;

                if (!Version.TryParse(parts[0], out _))
                    valid = false;
            }

            if (!valid)
                return BadRequest("Format for beta versions is: 1.2.3-beta");

            stable = false;
        }
        else
        {
            if (!Version.TryParse(request.ReleaseNumber, out _))
            {
                return BadRequest("Version number format is not syntactically valid version");
            }
        }

        // If the version suffix and stable flag disagree, that's an error
        if (stable != request.Stable)
        {
            return BadRequest(
                "Version number indicates either stable or beta version, but the stable checkbox state doesn't match");
        }

        var version = new LauncherThriveVersion(request.ReleaseNumber)
        {
            Stable = request.Stable,
            SupportsFailedStartupDetection = request.SupportsFailedStartupDetection,
        };

        if (await database.LauncherThriveVersions.FirstOrDefaultAsync(v => v.ReleaseNumber == version.ReleaseNumber) !=
            null)
        {
            return BadRequest("Version number is already in use");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New Thrive version {version.ReleaseNumber} created",
            PerformedById = user.Id,
        });

        await database.LauncherThriveVersions.AddAsync(version);

        await database.SaveChangesAsync();

        logger.LogInformation("New Thrive version {ReleaseNumber} ({Id}) created by {Email}", version.ReleaseNumber,
            version.Id, user.Email);
        return Ok(version.Id.ToString());
    }

    [NonAction]
    private void PreProcessMirrorRequest(LauncherDownloadMirrorDTO request)
    {
        if (string.IsNullOrWhiteSpace(request.ExtraDescription))
            request.ExtraDescription = null;

        if (string.IsNullOrWhiteSpace(request.BannerImageUrl))
            request.BannerImageUrl = null;
    }
}
