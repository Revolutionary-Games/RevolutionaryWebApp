using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
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
    using Shared;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/webhook/github")]
    public class GithubWebhookController : Controller
    {
        private readonly ILogger<GithubWebhookController> logger;
        private readonly ApplicationDbContext database;
        private readonly IBackgroundJobClient jobClient;

        public GithubWebhookController(ILogger<GithubWebhookController> logger, ApplicationDbContext database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
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

            if (!string.IsNullOrEmpty(data.Ref) && data.RefType != "branch" && !string.IsNullOrEmpty(data.After))
            {
                // This is a push (commit)
                logger.LogInformation("Received a push event for ref: {Ref}", data.Ref);

                bool matched = false;

                // Detect if this triggers any builds
                foreach (var project in await database.CiProjects.AsQueryable().Where(p =>
                    p.ProjectType == CIProjectType.Github && p.Enabled && !p.Deleted &&
                    p.RepositoryFullName == data.Repository.FullName).ToListAsync())
                {
                    matched = true;

                    // Detect next id
                    // TODO: is there a better way to get this?
                    var buildId = await database.CiBuilds.AsQueryable().Where(b => b.CiProjectId == project.Id)
                        .MaxAsync(b => b.CiBuildId) + 1;

                    var build = new CiBuild()
                    {
                        CiProjectId = project.Id,
                        CiBuildId = buildId,
                        CommitHash = data.After,
                        RemoteRef = data.Ref

                        // TODO: include info about the other commits (and the before commit)
                    };

                    await database.CiBuilds.AddAsync(build);
                    await database.SaveChangesAsync();

                    jobClient.Enqueue<CheckAndStartCIBuild>(x =>
                        x.Execute(build.CiProjectId, build.CiBuildId, CancellationToken.None));
                }

                if (!matched)
                    logger.LogWarning("Push event didn't match any repos: {Fullname}", data.Repository.FullName);
            } else if (!string.IsNullOrEmpty(data.Ref))
            {
                // This is a branch push (or maybe a tag?)
            }

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
        [JsonPropertyName("hook_id")]
        public long HookId { get; set; }

        public long Number { get; set; }

        public bool Merged { get; set; }

        public string Ref { get; set; }

        [JsonPropertyName("ref_type")]
        public string RefType { get; set; }

        /// <summary>
        ///   Commit before Ref
        /// </summary>
        public string Before { get; set; }

        /// <summary>
        ///   Commit on Ref after a push
        /// </summary>
        public string After { get; set; }

        public List<GithubCommit> Commits { get; set; }

        public GithubPusher Pusher { get; set; }

        [Required]
        public GithubHookInfo Hook { get; set; }

        public GithubRepository Repository { get; set; }

        public GithubOrganization Organization { get; set; }

        [Required]
        public GithubUserInfo Sender { get; set; }
    }

    public class GithubCommit
    {
        /// <summary>
        ///   Commit hash
        /// </summary>
        public string Id { get; set; }

        public string Timestamp { get; set; }

        public string Message { get; set; }

        public CommitAuthor Author { get; set; }
    }

    public class CommitAuthor
    {
        public string Name { get; set; }
        public string Email { get; set; }
    }

    public class GithubPusher
    {
        public string Name { get; set; }
        public string Email { get; set; }
    }

    public class GithubHookInfo
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
        [JsonPropertyName("full_name")]
        public string FullName { get; set; }

        public bool Private { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        /// <summary>
        ///   The API url
        /// </summary>
        public string Url { get; set; }

        public bool Fork { get; set; }

        [Required]
        [JsonPropertyName("clone_url")]
        public string CloneUrl { get; set; }

        [Required]
        [JsonPropertyName("default_branch")]
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

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        /// <summary>
        ///   Valid values seem to be "User" and "Organization"
        /// </summary>
        public string Type { get; set; } = "User";
    }
}
