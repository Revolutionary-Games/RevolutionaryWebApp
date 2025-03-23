namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Models;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;
using Utilities;

/// <summary>
///   Handles managing <see cref="SiteLayoutPart"/>
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SiteLayoutEditor, AllowAdmin = true)]
public class SiteLayoutController : Controller
{
    private readonly ILogger<SiteLayoutController> logger;
    private readonly NotificationsEnabledDb database;

    public SiteLayoutController(ILogger<SiteLayoutController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet]
    public async Task<List<SiteLayoutPartDTO>> GetSiteLayoutParts([Required] string sortColumn,
        [Required] SortDirection sortDirection)
    {
        IQueryable<SiteLayoutPart> query;

        try
        {
            query = database.SiteLayoutParts.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var raw = await query.ToListAsync();

        return raw.Select(i => i.GetDTO()).ToList();
    }

    [HttpPost]
    public async Task<IActionResult> CreateSiteLayoutPart([Required] [FromBody] SiteLayoutPartDTO request)
    {
        if (string.IsNullOrWhiteSpace(request.LinkTarget))
            request.LinkTarget = null;

        if (request.LinkTarget != null && !IsLinkValid(request.LinkTarget))
            return BadRequest("Invalid format for link target");

        // If creating a special item, force text to be specific
        if (request.DisplayMode is LayoutPartDisplayMode.Spacer or LayoutPartDisplayMode.Separator)
        {
            if (request.LinkTarget != null)
                return BadRequest("Spacer and separator parts cannot have a link target");

            request.AltText = request.DisplayMode switch
            {
                LayoutPartDisplayMode.Spacer => "SPACER",
                LayoutPartDisplayMode.Separator => "SEPARATOR",
                _ => throw new ArgumentOutOfRangeException(),
            };
        }

        // When creating social links, various settings are unsupported
        if (request.PartType == SiteLayoutPartType.SmallSocialsBar)
        {
            request.DisplayMode = LayoutPartDisplayMode.Normal;

            if (string.IsNullOrEmpty(request.LinkTarget))
                return BadRequest("Link target is required for social links");
        }

        var siteLayoutPart = new SiteLayoutPart(request.LinkTarget, request.AltText, request.PartType)
        {
            Enabled = request.Enabled,
            PartType = request.PartType,
            LinkTarget = request.LinkTarget,
            AltText = request.AltText,
            DisplayMode = request.DisplayMode,

            Order = request.Order,
        };

        if (await database.SiteLayoutParts.AnyAsync(i =>
                i.Order == request.Order && i.PartType == request.PartType))
        {
            return BadRequest("Order is already taken within the category");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (request.ImageId != null)
        {
            if (!Guid.TryParse(request.ImageId, out var parsedGuid))
                return BadRequest("Invalid image ID format (it must be a GUID)");

            var (valid, extension) = await VerifyImageIdIsValid(parsedGuid);

            if (!valid)
                return BadRequest("Invalid image ID (please check media browser to find image UUIDs)");

            // Images are automatically marked as using a foreign key on the GUID when referenced by a part
            siteLayoutPart.ImageId = parsedGuid;
            siteLayoutPart.ImageType = extension;
        }

        await database.SiteLayoutParts.AddAsync(siteLayoutPart);
        await database.AdminActions.AddAsync(
            new AdminAction($"New Site Layout Part created (type: {siteLayoutPart.PartType})")
            {
                PerformedById = user.Id,
            });

        await database.SaveChangesAsync();

        logger.LogInformation("New Site Layout Part {Id} created by {Email}", siteLayoutPart.Id, user.Email);

        return Ok();
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SiteLayoutPartDTO>> GetSiteLayoutPart([Required] long id)
    {
        var result = await database.SiteLayoutParts.FindAsync(id);

        if (result == null)
            return NotFound();

        return result.GetDTO();
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> UpdateSiteLayoutPart([Required] [FromBody] SiteLayoutPartDTO request)
    {
        var siteLayoutPart = await database.SiteLayoutParts.FindAsync(request.Id);

        if (siteLayoutPart == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.LinkTarget))
            request.LinkTarget = null;

        if (request.LinkTarget != null && !IsLinkValid(request.LinkTarget))
            return BadRequest("Invalid format for link target");

        if (request.Order != siteLayoutPart.Order)
        {
            // Check if the new order is available
            if (await database.SiteLayoutParts.AnyAsync(i =>
                    i.Order == request.Order && i.PartType == request.PartType))
            {
                return BadRequest("Order is already taken within the category");
            }
        }

        var oldImage = siteLayoutPart.ImageId;

        var user = HttpContext.AuthenticatedUserOrThrow();

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(siteLayoutPart, request);

        if (request.ImageId != null)
        {
            if (!Guid.TryParse(request.ImageId, out var parsedGuid))
                return BadRequest("Invalid image ID format (it must be a GUID)");

            var (valid, extension) = await VerifyImageIdIsValid(parsedGuid);

            if (!valid)
                return BadRequest("Invalid image ID (please check media browser to find image IDs)");

            if (siteLayoutPart.ImageId != parsedGuid)
            {
                siteLayoutPart.ImageId = parsedGuid;
                siteLayoutPart.ImageType = extension;
                changes = true;
                description += $", image changed from {oldImage} to {parsedGuid}";
            }
        }

        if (!changes)
            return Ok();

        siteLayoutPart.BumpUpdatedAt();

        await database.AdminActions.AddAsync(
            new AdminAction($"Site Layout Part {siteLayoutPart.Id} edited", description)
            {
                PerformedById = user.Id,
            });

        await database.SaveChangesAsync();

        logger.LogInformation("Site Layout Part {Id} edited by {Email}, changes: {Description}", siteLayoutPart.Id,
            user.Email, description);
        return Ok();
    }

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> ToggleSiteLayoutEnabled([Required] long id, [Required] bool enabled)
    {
        // Find the site layout part by ID
        var siteLayoutPart = await database.SiteLayoutParts.FindAsync(id);

        if (siteLayoutPart == null)
            return NotFound();

        if (siteLayoutPart.Enabled == enabled)
        {
            // Nothing to do
            return Ok("Already in desired state");
        }

        siteLayoutPart.Enabled = enabled;
        siteLayoutPart.BumpUpdatedAt();

        await database.SaveChangesAsync();

        logger.LogInformation("Site layout part {Id} enabled status set to {Enabled}", siteLayoutPart.Id,
            siteLayoutPart.Enabled);

        return Ok();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteSiteLayoutPart([Required] long id)
    {
        var siteLayoutPart = await database.SiteLayoutParts.FindAsync(id);

        if (siteLayoutPart == null)
            return NotFound();

        if (siteLayoutPart.Enabled)
            return BadRequest("Cannot delete enabled site layout part");

        var user = HttpContext.AuthenticatedUser()!;

        database.SiteLayoutParts.Remove(siteLayoutPart);

        // Store old data in case it needs to be restored
        await database.AdminActions.AddAsync(
            new AdminAction($"Site Layout Part {siteLayoutPart.Id} deleted", JsonSerializer.Serialize(siteLayoutPart))
            {
                PerformedById = user.Id,
            });

        await database.SaveChangesAsync();

        logger.LogInformation("Site Layout Part {Id} deleted by {Email}", siteLayoutPart.Id, user.Email);
        return Ok();
    }

    [NonAction]
    private async Task<(bool Valid, string? Extension)> VerifyImageIdIsValid(Guid imageId)
    {
        var image = await database.MediaFiles.FirstOrDefaultAsync(m => m.GlobalId == imageId);

        if (image == null)
            return (false, null);

        if (image.Deleted)
            return (false, null);

        return (true, Path.GetExtension(image.Name));
    }

    [NonAction]
    private bool IsLinkValid(string link)
    {
        // Whitespace is disallowed
        if (link.Contains(' '))
            return false;

        // ReSharper disable once HttpUrlsUsage
        if (link.StartsWith("http://") || link.StartsWith("https://"))
            return true;

        // Internal page link
        if (link.StartsWith("page:") && !link.Contains(' '))
            return true;

        // TODO: other page categories

        return false;
    }
}
