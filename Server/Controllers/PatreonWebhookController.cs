using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
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
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Services;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/webhook/patreon")]
    public class PatreonWebhookController : Controller
    {
        private readonly ILogger<PatreonWebhookController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        private enum EventType
        {
            Create,
            Update,
            Delete
        }

        public PatreonWebhookController(ILogger<PatreonWebhookController> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        [HttpPost]
        public async Task<IActionResult> PostWebhook(
            [Required] [MaxLength(200)] [Bind(Prefix = "webhook_id")] string webhookId)
        {
            var type = GetEventType();

            var settings = await database.PatreonSettings.AsQueryable()
                .FirstOrDefaultAsync(s => s.WebhookId == webhookId && s.Active == true);

            var verifiedPayload = await CheckSignature(settings);
            logger.LogTrace("Got patreon payload: {VerifiedPayload}", verifiedPayload);

            PatreonAPIObjectResponse data;

            try
            {
                data = JsonSerializer.Deserialize<PatreonAPIObjectResponse>(verifiedPayload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (data == null)
                    throw new Exception("deserialized data is null");
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to parse JSON in patreon webhook body: {@E}", e);
                throw new HttpResponseException()
                {
                    Value = new BasicJSONErrorResult("Invalid request", "Invalid JSON").ToString()
                };
            }

            var pledge = data.Data;

            if (pledge.Type != "pledge")
            {
                throw new HttpResponseException()
                {
                    Value = new BasicJSONErrorResult("Bad data", "Expected pledge object").ToString()
                };
            }

            var patronHookData = pledge.Relationships.Patron.Data;

            var userData = data.FindIncludedObject(patronHookData.Id);

            if (userData == null)
            {
                throw new HttpResponseException()
                {
                    Value = new BasicJSONErrorResult("Bad data", "Included objects didn't contain relevant user object")
                        .ToString()
                };
            }

            var email = userData.Attributes.Email;

            if (string.IsNullOrEmpty(email))
            {
                throw new HttpResponseException()
                {
                    Value = new BasicJSONErrorResult("Bad data", "User object is missing email")
                        .ToString()
                };
            }

            switch (type)
            {
                case EventType.Create:
                case EventType.Update:
                {
                    string rewardId = null;

                    try
                    {
                        // This was what the old code did, no clue why it would be necessary to unnecessarily
                        // look up the reward object...
                        // rewardId = data.FindIncludedObject(pledge.Relationships["reward"].Data.Id).Id;
                        rewardId = pledge.Relationships.Reward.Data.Id;
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning("Couldn't find reward ID in patreon webhook: {@E}", e);
                    }

                    await PatreonGroupHandler.HandlePatreonPledgeObject(pledge, userData, rewardId, database,
                        jobClient);
                    break;
                }
                case EventType.Delete:
                {
                    // Find relevant patron object and delete it
                    var patron = await database.Patrons.AsQueryable().FirstOrDefaultAsync(p => p.Email == email);

                    if (patron != null)
                    {
                        database.Patrons.Remove(patron);
                        jobClient.Schedule<CheckSSOUserSuspensionJob>(x => x.Execute(email, CancellationToken.None),
                            TimeSpan.FromSeconds(15));
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
                throw new HttpResponseException()
                {
                    Value = new BasicJSONErrorResult("Invalid request", "Missing X-Patreon-Event header").ToString()
                };
            }

            switch (header[0])
            {
                case "pledges:create":
                    return EventType.Create;
                case "pledges:update":
                    return EventType.Update;
                case "pledges:delete":
                    return EventType.Delete;
            }

            logger.LogWarning("Invalid event type in patreon webhook: {Header}", header[0]);

            throw new HttpResponseException()
            {
                Value = new BasicJSONErrorResult("Invalid request", "Unknown event type").ToString()
            };
        }

        [NonAction]
        private async Task<string> CheckSignature(PatreonSettings settings)
        {
            if (!HttpContext.Request.Headers.TryGetValue("X-Patreon-Signature", out StringValues header) ||
                header.Count != 1)
            {
                throw new HttpResponseException()
                {
                    Value = new BasicJSONErrorResult("Invalid request", "Missing X-Patreon-Signature header").ToString()
                };
            }

            var actualSignature = header[0];

            if (settings == null || string.IsNullOrEmpty(settings.WebhookSecret))
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = new BasicJSONErrorResult("Server configuration error", "Patreon webhook is not configured")
                        .ToString()
                };
            }

            var readBody = await Request.BodyReader.ReadAsync();

            // This line is needed to suppress "System.InvalidOperationException: Reading is already in progress."
            Request.BodyReader.AdvanceTo(readBody.Buffer.Start, readBody.Buffer.End);

            var rawPayload = readBody.Buffer.ToArray();

            var neededSignature = Convert.ToHexString(new HMACMD5(Encoding.UTF8.GetBytes(settings.WebhookSecret))
                .ComputeHash(rawPayload)).ToLowerInvariant();

            if (!SecurityHelpers.SlowEquals(neededSignature, actualSignature))
            {
                logger.LogWarning("Patreon webhook signature didn't match expected value");
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status403Forbidden,
                    Value = new BasicJSONErrorResult("Invalid signature",
                        "Payload signature does not match expected value").ToString()
                };
            }

            return Encoding.UTF8.GetString(rawPayload);
        }
    }
}
