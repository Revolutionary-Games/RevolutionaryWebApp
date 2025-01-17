namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
[Route("[controller]")]
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
        var siteLayoutPart = new SiteLayoutPart(request.LinkTarget, request.AltText, request.PartType)
        {
            Enabled = request.Enabled,
            PartType = request.PartType,
            LinkTarget = request.LinkTarget,
            AltText = request.AltText,

            Order = request.Order,
        };

        var user = HttpContext.AuthenticatedUser()!;

        if (request.ImageId != null)
        {
            if (!Guid.TryParse(request.ImageId, out var parsedGuid))
                return BadRequest("Invalid image ID format (it must be a GUID)");

            if (!await VerifyImageIdIsValid(parsedGuid))
                return BadRequest("Invalid image ID (please check media browser to find image IDs)");

            // Images are automatically marked as using a foreign key on the GUID when referenced by a part
            siteLayoutPart.ImageId = parsedGuid;
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

        var oldImage = siteLayoutPart.ImageId;

        var user = HttpContext.AuthenticatedUserOrThrow();

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(siteLayoutPart, request);

        if (request.ImageId != null)
        {
            if (!Guid.TryParse(request.ImageId, out var parsedGuid))
                return BadRequest("Invalid image ID format (it must be a GUID)");

            if (!await VerifyImageIdIsValid(parsedGuid))
                return BadRequest("Invalid image ID (please check media browser to find image IDs)");

            if (siteLayoutPart.ImageId != parsedGuid)
            {
                siteLayoutPart.ImageId = parsedGuid;
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
    private async Task<bool> VerifyImageIdIsValid(Guid imageId)
    {
        var image = await database.MediaFiles.FirstOrDefaultAsync(m => m.GlobalId == imageId);

        if (image == null)
            return false;

        if (image.Deleted)
            return false;

        return true;
    }
}
