namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

/// <summary>
///   Admin-only controller for managing Patreon settings
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
public class PatreonConfigurationController : Controller
{
    private readonly ILogger<PatreonConfigurationController> logger;
    private readonly NotificationsEnabledDb database;

    public PatreonConfigurationController(ILogger<PatreonConfigurationController> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet]
    public async Task<IEnumerable<PatreonSettingsDTO>> Get()
    {
        // GetDTO call safely filters attributes here
        return (await database.PatreonSettings.OrderBy(s => s.Id).ToListAsync()).Select(s => s.GetDTO()).ToList();
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        if (await database.PatreonSettings.AnyAsync())
            return BadRequest("Patreon settings already exist");

        var user = HttpContext.AuthenticatedUserOrThrow();

        var settings = new PatreonSettings
        {
            Active = false,
        };

        await database.PatreonSettings.AddAsync(settings);

        await database.AdminActions.AddAsync(new AdminAction("Patreon settings created")
        {
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Patreon settings created by {Email}", user.Email);

        return Ok();
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update([Required] long id, [Required] [FromBody] PatreonSettingsDTO request)
    {
        var settings = await database.PatreonSettings.FindAsync(id);
        if (settings == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(settings, request);

        // Apply special token changes
        if (!string.IsNullOrEmpty(request.CreatorToken))
        {
            if (settings.CreatorToken != request.CreatorToken)
            {
                settings.CreatorToken = request.CreatorToken;
                changes = true;
                description += (string.IsNullOrEmpty(description) ? string.Empty : ", ") + "CreatorToken changed";
            }
        }

        if (!string.IsNullOrEmpty(request.WebhookSecret))
        {
            if (settings.WebhookSecret != request.WebhookSecret)
            {
                settings.WebhookSecret = request.WebhookSecret;
                changes = true;
                description += (string.IsNullOrEmpty(description) ? string.Empty : ", ") + "WebhookSecret changed";
            }
        }

        if (!changes)
            return Ok();

        settings.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction($"Patreon settings {settings.Id} edited", description)
        {
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Patreon settings {Id} edited by {Email}, changes: {Description}", settings.Id,
            user.Email, description);

        return Ok();
    }

    [HttpPost("verify")]
    public async Task<ActionResult<string>> VerifyToken([Required] [FromBody] RemotePatreonRequest request)
    {
        try
        {
            var token = await GetToken(request);
            using var api = new PatreonCreatorAPI(token);
            var details = await api.GetOwnDetails(HttpContext.RequestAborted);
            return
                $"Token is valid. Authenticated as: {details.Data.Attributes.FullName} ({details.Data.Attributes.Email})";
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to verify Patreon token: {@E}", e);
            return BadRequest("Failed to verify token: " + e.Message);
        }
    }

    [HttpPost("campaigns")]
    public async Task<ActionResult<List<PatreonObjectData>>> GetCampaigns([Required] [FromBody] RemotePatreonRequest request)
    {
        try
        {
            var token = await GetToken(request);
            using var api = new PatreonCreatorAPI(token);
            return await api.GetCampaigns(HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to fetch Patreon campaigns: {@E}", e);
            return BadRequest("Failed to fetch campaigns: " + e.Message);
        }
    }

    [HttpPost("rewards")]
    public async Task<ActionResult<List<PatreonObjectData>>> GetRewards([Required] [FromBody] PatreonRewardsRequest request)
    {
        try
        {
            var token = await GetToken(request);
            using var api = new PatreonCreatorAPI(token);
            return await api.GetRewards(request.CampaignId, HttpContext.RequestAborted);
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to fetch Patreon rewards: {@E}", e);
            return BadRequest("Failed to fetch rewards: " + e.Message);
        }
    }

    [NonAction]
    private async Task<string> GetToken(RemotePatreonRequest request)
    {
        if (!string.IsNullOrEmpty(request.Token))
            return request.Token;

        var settings = await database.PatreonSettings.FindAsync(request.Id);

        if (settings == null)
            throw new Exception("Patreon settings not found for token lookup");

        if (string.IsNullOrEmpty(settings.CreatorToken))
            throw new Exception("Patreon settings does not have a creator token saved");

        return settings.CreatorToken;
    }
}
