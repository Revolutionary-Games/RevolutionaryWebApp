using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Buffers;
    using System.ComponentModel.DataAnnotations;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Authorization;
    using Filters;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/webhook/github")]
    public class GithubWebhookController : Controller
    {
        private readonly ILogger<GithubWebhookController> logger;
        private readonly ApplicationDbContext database;

        public GithubWebhookController(ILogger<GithubWebhookController> logger, ApplicationDbContext database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpPost]
        public async Task<IActionResult> ReceiveWebhook()
        {
            var hook = await database.GithubWebhooks.FindAsync(AppInfo.SingleResourceTableRowId);

            if (hook == null)
            {
                logger.LogWarning("Github webhook secret is not configured, can't process webhook");
                return BadRequest("Incorrect secret");
            }

            var payload = await CheckSignature(hook);

            GithubWebhookContent data;
            try
            {
                data = JsonSerializer.Deserialize<GithubWebhookContent>(payload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (data == null)
                    throw new Exception("deserialized value is null");
            }
            catch (Exception e)
            {
                logger.LogWarning("Error deserializing github webhook: {@E}", e);
                throw new HttpResponseException()
                {
                    Value = new BasicJSONErrorResult("Invalid content",
                        "Failed to deserialize payload").ToString()
                };
            }

            // Didn't find anything to process here

            // TODO: should this always be updated. Github might send us quite a few events if we subscribe to them all
            hook.LastUsed = DateTime.UtcNow;
            await database.SaveChangesAsync();

            return Ok();
        }

        [NonAction]
        private async Task<string> CheckSignature(GithubWebhook hook)
        {
            if (!HttpContext.Request.Headers.TryGetValue("X-Hub-Signature-256", out StringValues header) ||
                header.Count != 1)
            {
                throw new HttpResponseException()
                {
                    Value = new BasicJSONErrorResult("Invalid request", "Missing X-Hub-Signature-256 header").ToString()
                };
            }

            var actualSignature = header[0];

            var readBody = await Request.BodyReader.ReadAsync();

            // This line is needed to suppress "System.InvalidOperationException: Reading is already in progress."
            Request.BodyReader.AdvanceTo(readBody.Buffer.Start, readBody.Buffer.End);

            var rawPayload = readBody.Buffer.ToArray();

            var neededSignature = "sha256=" + Convert.ToHexString(new HMACSHA256(Encoding.UTF8.GetBytes(hook.Secret))
                .ComputeHash(rawPayload)).ToLowerInvariant();

            if (!SecurityHelpers.SlowEquals(neededSignature, actualSignature))
            {
                logger.LogWarning(
                    "Github webhook signature ({ActualSignature}) didn't match expected value ({NeededSignature})",
                    actualSignature, neededSignature);
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

    public class GithubWebhookContent
    {
        public string Action { get; set; }

        [Required]
        public long HookId { get; set; }

        public long Number { get; set; }

        public bool Merged { get; set; }

        [Required]
        public GithubHookContent Hook { get; set; }

        public GithubRepository Repository { get; set; }

        public GithubOrganization Organization { get; set; }

        [Required]
        public GithubUserInfo Sender { get; set; }
    }

    public class GithubHookContent
    {
        [Required]
        public string Type { get; set; }

        [Required]
        public long Id { get; set; }
    }

    public class GithubRepository
    {
        public long Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string FullName { get; set; }

        public bool Private { get; set; }

        public string HtmlUrl { get; set; }

        /// <summary>
        ///   The API url
        /// </summary>
        public string Url { get; set; }

        public bool Fork { get; set; }

        [Required]
        public string CloneUrl { get; set; }

        [Required]
        public string DefaultBranch { get; set; }
    }

    public class GithubOrganization
    {
    }

    public class GithubUserInfo
    {
        /// <summary>
        ///   This is the username
        /// </summary>
        [Required]
        public string Login { get; set; }

        [Required]
        public long Id { get; set; }

        public string HtmlUrl { get; set; }

        public string Type { get; set; } = "User";
    }
}
