namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Models;
using Shared.Models;
using SharedBase.Utilities;
using Utilities;

[ApiController]
[Route("api/v1/webhook/patreon")]
public class PatreonWebhookController : Controller
{
    private readonly ILogger<PatreonWebhookController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public PatreonWebhookController(ILogger<PatreonWebhookController> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    private enum EventType
    {
        Create,
        Update,
        Delete,
    }

    [HttpPost]
    public async Task<IActionResult> PostWebhook(
        [Required] [MaxLength(200)] [Bind(Prefix = "webhook_id")] string webhookId)
    {
        var type = GetEventType();

        var settings =
            await database.PatreonSettings.FirstOrDefaultAsync(s => s.WebhookId == webhookId && s.Active);

        if (settings == null)
            return this.WorkingForbid("Invalid hook id");

        var verifiedPayload = await CheckSignature(settings);
        logger.LogTrace("Got patreon payload: {VerifiedPayload}", verifiedPayload);

        PatreonAPIObjectResponse data;

        try
        {
            data = JsonSerializer.Deserialize<PatreonAPIObjectResponse>(verifiedPayload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new NullDecodedJsonException();
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to parse JSON in patreon webhook body: {@E}", e);
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Invalid request", "Invalid JSON").ToString(),
            };
        }

        var member = data.Data;

        if (member.Type != "member")
        {
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Bad data", "Expected pledge object").ToString(),
            };
        }

        var userHookData = member.Relationships?.User?.Data;

        if (userHookData == null)
        {
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Bad data", "Associated patron data not included").ToString(),
            };
        }

        var userData = data.FindIncludedObject(userHookData.Id, "user");

        if (userData == null)
        {
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Bad data", "Included objects didn't contain relevant user object")
                    .ToString(),
            };
        }

        var email = member.Attributes.Email ?? userData.Attributes.Email;
        if (string.IsNullOrEmpty(email))
            email = $"noreply+patron-{Uri.EscapeDataString(userData.Id)}@{PatreonGroupHandler.SyntheticEmailDomain}";

        userData.Attributes.Email = email;

        switch (type)
        {
            case EventType.Create:
            case EventType.Update:
            {
                var entitledTiers = member.Relationships?.CurrentlyEntitledTiers?.Data
                    .Select(tier => data.FindIncludedObject(tier.Id, "tier"))
                    .Where(tier => tier != null)
                    .Cast<PatreonObjectData>()
                    .ToList() ?? new List<PatreonObjectData>();

                if (entitledTiers.Count == 0)
                    member.Attributes.PatronStatus = "former_patron";

                await PatreonGroupHandler.HandlePatreonMemberObject(member, userData, entitledTiers, database,
                    jobClient);
                break;
            }

            case EventType.Delete:
            {
                // Find relevant patron object and delete it
                var patron = await database.Patrons.FirstOrDefaultAsync(p =>
                    p.PatreonMemberId == member.Id || p.PatreonUserId == userData.Id || p.Email == email);

                if (patron != null)
                {
                    email = patron.Email;
                    database.Patrons.Remove(patron);
                    jobClient.Schedule<ApplyUserAutomaticGroupsJob>(x => x.Execute(email, CancellationToken.None),
                        TimeSpan.FromSeconds(30));
                }
                else
                {
                    logger.LogWarning("Could not find patron to delete by email: {Email}", email);
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException();
        }

        settings.LastWebhook = DateTime.UtcNow;

        await database.SaveChangesAsync();

        // Queue a job to update the status for the relevant user
        jobClient.Enqueue<ApplySinglePatronGroupsJob>(x => x.Execute(email, CancellationToken.None));

        return Ok();
    }

    [NonAction]
    private EventType GetEventType()
    {
        if (!HttpContext.Request.Headers.TryGetValue("X-Patreon-Event", out StringValues header) ||
            header.Count != 1)
        {
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Invalid request", "Missing X-Patreon-Event header").ToString(),
            };
        }

        switch (header[0])
        {
            case "members:create":
            case "members:pledge:create":
                return EventType.Create;
            case "members:update":
            case "members:pledge:update":
                return EventType.Update;
            case "members:delete":
            case "members:pledge:delete":
                return EventType.Delete;
        }

        logger.LogWarning("Invalid event type in patreon webhook: {Header}", header[0]);

        throw new HttpResponseException
        {
            Value = new BasicJSONErrorResult("Invalid request", "Unknown event type").ToString(),
        };
    }

    [NonAction]
    private async Task<string> CheckSignature(PatreonSettings settings)
    {
        if (!HttpContext.Request.Headers.TryGetValue("X-Patreon-Signature", out StringValues header) ||
            header.Count != 1)
        {
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Invalid request", "Missing X-Patreon-Signature header").ToString(),
            };
        }

        var actualSignature = header[0];

        if (settings == null || string.IsNullOrEmpty(settings.WebhookSecret))
        {
            throw new HttpResponseException
            {
                Status = StatusCodes.Status500InternalServerError,
                Value = new BasicJSONErrorResult("Server configuration error", "Patreon webhook is not configured")
                    .ToString(),
            };
        }

        var readBody = await Request.ReadBodyAsync();
        var rawPayload = readBody.Buffer.ToArray();

        var neededSignature = Convert.ToHexString(new HMACMD5(Encoding.UTF8.GetBytes(settings.WebhookSecret))
            .ComputeHash(rawPayload)).ToLowerInvariant();

        if (!SecurityHelpers.SlowEquals(neededSignature, actualSignature))
        {
            logger.LogWarning("Patreon webhook signature didn't match expected value");
            throw new HttpResponseException
            {
                Status = StatusCodes.Status403Forbidden,
                Value = new BasicJSONErrorResult("Invalid signature",
                    "Payload signature does not match expected value").ToString(),
            };
        }

        return Encoding.UTF8.GetString(rawPayload);
    }
}
