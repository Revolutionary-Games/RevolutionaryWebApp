namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using RecursiveDataAnnotationsValidation;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using SharedBase.Models;
using SharedBase.Utilities;
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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

        foreach (var download in currentData.LauncherVersion.AutoUpdateDownloads.Values.Concat(currentData.Versions
                     .SelectMany(v => v.Platforms).Select(p => p.Value)))
        {
            foreach (var (key, url) in download.Mirrors)
            {
                if (url.ToString().Length > GlobalConstants.DEFAULT_MAX_LENGTH_FOR_TO_STRING_ATTRIBUTE)
                {
                    validationError = $"Too long download URL (mirror: {key}): {url}";
                    break;
                }
            }
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

    [HttpGet("keyExpiry")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public ActionResult<DateTime> GetSigningExpiry()
    {
        var expiry = configuration["Launcher:InfoKeyExpires"];

        if (string.IsNullOrWhiteSpace(expiry))
            return NotFound();

        if (!DateTime.TryParse(expiry, CultureInfo.InvariantCulture, out var expiryTime))
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Value = "Parsing expiry time failed",
            };
        }

        return expiryTime;
    }

    [HttpGet("mirrors")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<LauncherDownloadMirrorDTO>> GetMirror(long id)
    {
        var mirror = await database.LauncherDownloadMirrors.FindAsync(id);

        if (mirror == null)
            return NotFound();

        return mirror.GetDTO();
    }

    [HttpPut("mirrors/{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<LauncherLauncherVersionDTO>> GetLauncherVersion(long id)
    {
        var version = await database.LauncherLauncherVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        return version.GetDTO();
    }

    [HttpDelete("launcherVersions/{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
            previous.SetLatestAt = null;
            previous.BumpUpdatedAt();

            // We have to save here due to the unique index on latest
            await database.SaveChangesAsync();
        }

        version.Latest = true;
        version.SetLatestAt = DateTime.UtcNow;
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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

    // Launcher Launcher version channels

    [HttpGet("launcherVersions/{id:long}/channels")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<PagedResult<LauncherVersionAutoUpdateChannelDTO>> GetLauncherVersionChannels(long id,
        [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<LauncherVersionAutoUpdateChannel> query;

        try
        {
            query = database.LauncherVersionAutoUpdateChannels.Where(c => c.VersionId == id).AsNoTracking()
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("launcherVersions/{id:long}/channels/{channel:int}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<LauncherVersionAutoUpdateChannelDTO>> GetLauncherVersionChannel(long id,
        int channel)
    {
        var channelEnumValue = ChannelFromInt(channel);

        var channelObject = await database.LauncherVersionAutoUpdateChannels.FindAsync(id, channelEnumValue);

        if (channelObject == null)
            return NotFound();

        return channelObject.GetDTO();
    }

    [HttpDelete("launcherVersions/{id:long}/channels/{channel:int}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> DeleteLauncherVersionChannel(long id, int channel)
    {
        var channelEnumValue = ChannelFromInt(channel);

        var channelObject = await database.LauncherVersionAutoUpdateChannels.FindAsync(id, channelEnumValue);

        if (channelObject == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Launcher version's ({channelObject.VersionId}) channel {channelEnumValue} deleted",
            PerformedById = user.Id,
        });

        database.LauncherVersionAutoUpdateChannels.Remove(channelObject);

        await database.SaveChangesAsync();

        logger.LogInformation("Launcher version's ({Id}) channel {Channel} deleted by {Email}", channelObject.VersionId,
            channelEnumValue, user.Email);
        return Ok();
    }

    [HttpPost("launcherVersions/{id:long}/channels")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> CreateLauncherVersionChannel(long id,
        [Required] [FromBody] LauncherVersionAutoUpdateChannelDTO request)
    {
        // A bit of a silly way to ensure the channel is correct
        ChannelFromInt((int)request.Channel);

        var version = await database.LauncherLauncherVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        var channel = new LauncherVersionAutoUpdateChannel(version.Id, request.Channel, request.FileSha3);

        if (await database.LauncherVersionAutoUpdateChannels.FirstOrDefaultAsync(c =>
                c.VersionId == channel.VersionId && c.Channel == channel.Channel) !=
            null)
        {
            return BadRequest("Channel type for this version is already in use");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New channel {channel.Channel} created for launcher version {version.Version}",
            PerformedById = user.Id,
        });

        await database.LauncherVersionAutoUpdateChannels.AddAsync(channel);
        version.BumpUpdatedAt();

        await database.SaveChangesAsync();

        logger.LogInformation("New channel {Channel} created for launcher version {Version} ({Id}) by {Email}",
            channel.Channel, version.Version, version.Id, user.Email);
        return Ok();
    }

    // Launcher Launcher version channel downloads

    [HttpGet("launcherVersions/{id:long}/channels/{channel:int}/downloads")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<PagedResult<LauncherVersionDownloadDTO>> GetLauncherVersionChannelDownloads(long id,
        int channel, [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        var channelEnumValue = ChannelFromInt(channel);

        IQueryable<LauncherVersionDownload> query;

        try
        {
            query = database.LauncherVersionDownloads.Include(d => d.Mirror)
                .Where(d => d.VersionId == id && d.Channel == channelEnumValue).AsNoTracking()
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("launcherVersions/{id:long}/channels/{channel:int}/downloads/{mirrorId:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<LauncherVersionDownloadDTO>> GetLauncherVersionChannelDownload(long id,
        int channel, long mirrorId)
    {
        var channelEnumValue = ChannelFromInt(channel);

        var channelObject = await database.LauncherVersionDownloads.Include(d => d.Mirror)
            .FirstOrDefaultAsync(d => d.VersionId == id && d.Channel == channelEnumValue && d.MirrorId == mirrorId);

        if (channelObject == null)
            return NotFound();

        return channelObject.GetDTO();
    }

    [HttpDelete("launcherVersions/{id:long}/channels/{channel:int}/downloads/{mirrorId:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> DeleteLauncherVersionChannelDownload(long id, int channel, long mirrorId)
    {
        var channelEnumValue = ChannelFromInt(channel);

        var download = await database.LauncherVersionDownloads.Include(d => d.Mirror)
            .FirstOrDefaultAsync(d => d.VersionId == id && d.Channel == channelEnumValue && d.MirrorId == mirrorId);

        if (download == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message =
                $"Download with mirror {download.Mirror.InternalName} for Launcher version's " +
                $"({download.VersionId}) channel {channelEnumValue} deleted",
            PerformedById = user.Id,
        });

        database.LauncherVersionDownloads.Remove(download);

        await database.SaveChangesAsync();

        logger.LogInformation(
            "Download with mirror {InternalName} for Launcher version's ({Id}) channel {Channel} deleted by {Email}",
            download.Mirror.InternalName, download.VersionId, channelEnumValue, user.Email);
        return Ok();
    }

    [HttpPost("launcherVersions/{id:long}/channels/{channel:int}/downloads")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> CreateLauncherVersionChannelDownload(long id, int channel,
        [Required] [FromBody] LauncherVersionDownloadDTO request)
    {
        // A bit of a silly way to ensure the channel is correct
        var channelEnumValue = ChannelFromInt(channel);

        var channelObject = await database.LauncherVersionAutoUpdateChannels.FindAsync(id, channelEnumValue);

        if (channelObject == null)
            return NotFound();

        // Parse mirror name
        if (!string.IsNullOrEmpty(request.MirrorName))
        {
            var mirror =
                await database.LauncherDownloadMirrors.FirstOrDefaultAsync(m => m.InternalName == request.MirrorName);

            if (mirror == null)
            {
                return BadRequest("Invalid mirror internal name");
            }

            request.MirrorId = mirror.Id;
        }

        if (request.MirrorId < 0)
            return BadRequest("No mirror Id set, and no mirror name was set to detect it from");

        var download = new LauncherVersionDownload(channelObject.VersionId, channelObject.Channel, request.MirrorId,
            new Uri(request.DownloadUrl));

        if (await database.LauncherVersionDownloads.FirstOrDefaultAsync(d =>
                d.VersionId == download.VersionId && d.Channel == download.Channel &&
                d.MirrorId == download.MirrorId) != null)
        {
            return BadRequest("Download mirror is in use already for this channel and version");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message =
                $"New download mirror ({download.MirrorId}) url specified for channel {download.Channel} " +
                $"in a launcher version ({channelObject.VersionId})",
            PerformedById = user.Id,
        });

        await database.LauncherVersionDownloads.AddAsync(download);

        await database.SaveChangesAsync();

        logger.LogInformation(
            "New download with mirror {MirrorId} ({DownloadUrl}) in channel {Channel} created " +
            "for launcher version ({Id}) by {Email}",
            download.MirrorId, download.DownloadUrl,
            download.Channel, channelObject.VersionId, user.Email);
        return Ok();
    }

    // Launcher Thrive versions

    [HttpGet("thriveVersions")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<LauncherThriveVersionDTO>> GetThriveVersion(long id)
    {
        var version = await database.LauncherThriveVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        return version.GetDTO();
    }

    [HttpPut("thriveVersions/{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
                $"Thrive version {version.ReleaseNumber} (previous: {previous?.ReleaseNumber}) " +
                $"is now latest for stable: {version.Stable}",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();
        await database.Database.CommitTransactionAsync();

        logger.LogInformation("Thrive version {Id} is now set latest by {Email}", version.Id, user.Email);
        return Ok();
    }

    [HttpPost("thriveVersions/{id:long}/enable")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
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

    // Launcher Thrive version platforms

    [HttpGet("thriveVersions/{id:long}/platforms")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<PagedResult<LauncherThriveVersionPlatformDTO>> GetThriveVersionPlatforms(long id,
        [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<LauncherThriveVersionPlatform> query;

        try
        {
            query = database.LauncherThriveVersionPlatforms.Where(p => p.VersionId == id).AsNoTracking()
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("thriveVersions/{id:long}/platforms/{platform:int}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<LauncherThriveVersionPlatformDTO>> GetThriveVersionPlatform(long id,
        int platform)
    {
        var platformEnumValue = PlatformFromInt(platform);

        var platformObject = await database.LauncherThriveVersionPlatforms.Include(p => p.Version)
            .FirstOrDefaultAsync(p => p.VersionId == id && p.Platform == platformEnumValue);

        if (platformObject == null)
            return NotFound();

        return platformObject.GetDTO();
    }

    [HttpPut("thriveVersions/{id:long}/platforms/{platform:int}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> UpdateThriveVersionPlatform(long id, int platform,
        [Required] [FromBody] LauncherThriveVersionPlatformDTO request)
    {
        var platformEnumValue = PlatformFromInt(platform);

        var platformObject = await database.LauncherThriveVersionPlatforms.FindAsync(id, platformEnumValue);

        if (platformObject == null)
            return NotFound();

        // We get the version here as well so we can bump its updated time
        var version = await database.LauncherThriveVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(platformObject, request);

        if (!changes)
            return Ok();

        version.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Thrive version {version.ReleaseNumber} ({version.Id}) platform {platformEnumValue} edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Thrive version's ({Id}) platform {PlatformObject} edited by {Email}, " +
            "changes: {Description}", version.Id, platformEnumValue, user.Email, description);

        return Ok();
    }

    [HttpDelete("thriveVersions/{id:long}/platforms/{platform:int}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> DeleteThriveVersionPlatform(long id, int platform)
    {
        var platformEnumValue = PlatformFromInt(platform);

        var platformObject = await database.LauncherThriveVersionPlatforms.FindAsync(id, platformEnumValue);

        if (platformObject == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Thrive version's ({platformObject.VersionId}) platform {platformEnumValue} deleted",
            PerformedById = user.Id,
        });

        database.LauncherThriveVersionPlatforms.Remove(platformObject);

        await database.SaveChangesAsync();

        logger.LogInformation("Thrive version's ({Id}) platform {Platform} deleted by {Email}",
            platformObject.VersionId,
            platformEnumValue, user.Email);
        return Ok();
    }

    [HttpPost("thriveVersions/{id:long}/platforms")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> CreateThriveVersionPlatform(long id,
        [Required] [FromBody] LauncherThriveVersionPlatformDTO request)
    {
        // A bit of a silly way to ensure the platform is correct
        PlatformFromInt((int)request.Platform);

        var version = await database.LauncherThriveVersions.FindAsync(id);

        if (version == null)
            return NotFound();

        var platform =
            new LauncherThriveVersionPlatform(version.Id, request.Platform, request.FileSha3, request.LocalFileName);

        if (await database.LauncherThriveVersionPlatforms.FirstOrDefaultAsync(c =>
                c.VersionId == platform.VersionId && c.Platform == platform.Platform) !=
            null)
        {
            return BadRequest("Platform type for this version is already in use");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New platform {platform.Platform} created for thrive version {version.ReleaseNumber}",
            PerformedById = user.Id,
        });

        await database.LauncherThriveVersionPlatforms.AddAsync(platform);
        version.BumpUpdatedAt();

        await database.SaveChangesAsync();

        logger.LogInformation("New platform {Platform} created for thrive version {ReleaseNumber} ({Id}) by {Email}",
            platform.Platform, version.ReleaseNumber, version.Id, user.Email);
        return Ok();
    }

    // Launcher thrive version platform downloads

    [HttpGet("thriveVersions/{id:long}/platforms/{platform:int}/downloads")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<PagedResult<LauncherThriveVersionDownloadDTO>> GetThriveVersionPlatformDownloads(long id,
        int platform, [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        var platformEnumValue = PlatformFromInt(platform);

        IQueryable<LauncherThriveVersionDownload> query;

        try
        {
            query = database.LauncherThriveVersionDownloads.Include(d => d.Mirror)
                .Where(d => d.VersionId == id && d.Platform == platformEnumValue).AsNoTracking()
                .OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpGet("thriveVersions/{id:long}/platforms/{platform:int}/downloads/{mirrorId:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<LauncherThriveVersionDownloadDTO>> GetThriveVersionPlatformDownload(long id,
        int platform, long mirrorId)
    {
        var platformEnumValue = PlatformFromInt(platform);

        var platformObject = await database.LauncherThriveVersionDownloads.Include(d => d.Mirror)
            .FirstOrDefaultAsync(d => d.VersionId == id && d.Platform == platformEnumValue && d.MirrorId == mirrorId);

        if (platformObject == null)
            return NotFound();

        return platformObject.GetDTO();
    }

    [HttpDelete("thriveVersions/{id:long}/platforms/{platform:int}/downloads/{mirrorId:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> DeleteThriveVersionPlatformDownload(long id, int platform, long mirrorId)
    {
        var platformEnumValue = PlatformFromInt(platform);

        var download = await database.LauncherThriveVersionDownloads.Include(d => d.Mirror)
            .FirstOrDefaultAsync(d => d.VersionId == id && d.Platform == platformEnumValue && d.MirrorId == mirrorId);

        if (download == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message =
                $"Download with mirror {download.Mirror.InternalName} for Thrive version's " +
                $"({download.VersionId}) platform {platformEnumValue} deleted",
            PerformedById = user.Id,
        });

        database.LauncherThriveVersionDownloads.Remove(download);

        await database.SaveChangesAsync();

        logger.LogInformation(
            "Download with mirror {InternalName} for Thrive version's ({Id}) platform {Platform} deleted by {Email}",
            download.Mirror.InternalName, download.VersionId, platformEnumValue, user.Email);
        return Ok();
    }

    [HttpPost("thriveVersions/{id:long}/platforms/{platform:int}/downloads")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<IActionResult> CreateThriveVersionPlatformDownload(long id, int platform,
        [Required] [FromBody] LauncherThriveVersionDownloadDTO request)
    {
        // A bit of a silly way to ensure the platform is correct
        var platformEnumValue = PlatformFromInt(platform);

        var platformObject = await database.LauncherThriveVersionPlatforms.FindAsync(id, platformEnumValue);

        if (platformObject == null)
            return NotFound();

        // Parse mirror name
        if (!string.IsNullOrEmpty(request.MirrorName))
        {
            var mirror =
                await database.LauncherDownloadMirrors.FirstOrDefaultAsync(m => m.InternalName == request.MirrorName);

            if (mirror == null)
            {
                return BadRequest("Invalid mirror internal name");
            }

            request.MirrorId = mirror.Id;
        }

        if (request.MirrorId < 0)
            return BadRequest("No mirror Id set, and no mirror name was set to detect it from");

        var download = new LauncherThriveVersionDownload(platformObject.VersionId, platformObject.Platform,
            request.MirrorId,
            new Uri(request.DownloadUrl));

        if (await database.LauncherThriveVersionDownloads.FirstOrDefaultAsync(d =>
                d.VersionId == download.VersionId && d.Platform == download.Platform &&
                d.MirrorId == download.MirrorId) != null)
        {
            return BadRequest("Download mirror is in use already for this platform and version");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message =
                $"New download mirror ({download.MirrorId}) url specified for platform {download.Platform} " +
                $"in a thrive version ({platformObject.VersionId})",
            PerformedById = user.Id,
        });

        await database.LauncherThriveVersionDownloads.AddAsync(download);

        await database.SaveChangesAsync();

        logger.LogInformation(
            "New download with mirror {MirrorId} ({DownloadUrl}) for platform {Platform} created " +
            "in thrive version ({Id}) by {Email}",
            download.MirrorId, download.DownloadUrl,
            download.Platform, platformObject.VersionId, user.Email);
        return Ok();
    }

    // End of endpoints

    [NonAction]
    private static LauncherAutoUpdateChannel ChannelFromInt(int channel)
    {
        var channelEnumValue = (LauncherAutoUpdateChannel)channel;

        if (!Enum.IsDefined(channelEnumValue))
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.BadRequest,
                Value = "Invalid channel enum value",
            };
        }

        return channelEnumValue;
    }

    [NonAction]
    private static PackagePlatform PlatformFromInt(int platform)
    {
        var platformEnumValue = (PackagePlatform)platform;

        if (!Enum.IsDefined(platformEnumValue))
        {
            throw new HttpResponseException
            {
                Status = (int)HttpStatusCode.BadRequest,
                Value = "Invalid platform enum value",
            };
        }

        return platformEnumValue;
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
