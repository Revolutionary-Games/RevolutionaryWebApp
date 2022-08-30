namespace ThriveDevCenter.Server.Controllers;

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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Models;
using Services;
using Shared;
using Shared.Converters;
using Shared.Models;
using SharedBase.Utilities;
using Utilities;

[ApiController]
[Route("api/v1/webhook/github")]
public class GithubWebhookController : Controller
{
    private readonly ILogger<GithubWebhookController> logger;

    /// <summary>
    ///   Needs to be notifications enabled to update the builds list on a CI project's page
    /// </summary>
    private readonly NotificationsEnabledDb database;

    private readonly IBackgroundJobClient jobClient;

    public GithubWebhookController(ILogger<GithubWebhookController> logger, NotificationsEnabledDb database,
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
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new NullDecodedJsonException();

            if (data == null)
                throw new Exception("deserialized value is null");
        }
        catch (Exception e)
        {
            logger.LogWarning("Error deserializing github webhook: {@E}", e);
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Invalid content",
                    "Failed to deserialize payload").ToString(),
            };
        }

        if (!HttpContext.Request.Headers.TryGetValue("X-GitHub-Event", out StringValues typeHeader) ||
            typeHeader.Count != 1)
        {
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Invalid request", "Missing X-GitHub-Event header").ToString(),
            };
        }

        var type = typeHeader[0];

        // TODO: check type on these first two event detections as well
        if (!string.IsNullOrEmpty(data.Ref) && data.RefType != "branch" && !string.IsNullOrEmpty(data.After))
        {
            // This is a push (commit)
            logger.LogInformation("Received a push event for ref: {Ref}", data.Ref);

            if (data.Deleted || data.After == AppInfo.NoCommitHash)
            {
                logger.LogInformation("Push was about a deleted thing");
            }
            else
            {
                if (data.Before == AppInfo.NoCommitHash)
                {
                    logger.LogInformation(
                        "Received a push (probably a new branch) with no before set, setting to the after commit");
                    data.Before = data.After;
                }

                if (data.Repository == null)
                {
                    throw new HttpResponseException
                    {
                        Value = new BasicJSONErrorResult("Invalid request",
                            "Repository is needed for this event type").ToString(),
                    };
                }

                bool matched = false;

                // Detect if this triggers any builds
                foreach (var project in await database.CiProjects.Where(p =>
                             p.ProjectType == CIProjectType.Github && p.Enabled && !p.Deleted &&
                             p.RepositoryFullName == data.Repository.FullName).ToListAsync())
                {
                    matched = true;

                    // Detect next id
                    var previousBuildId = await database.CiBuilds.Where(b => b.CiProjectId == project.Id)
                        .MaxAsync(b => (long?)b.CiBuildId) ?? 0;

                    var build = new CiBuild
                    {
                        CiProjectId = project.Id,
                        CiBuildId = ++previousBuildId,
                        CommitHash = data.After,
                        RemoteRef = data.Ref,
                        Branch = GitRunHelpers.ParseRefBranch(data.Ref),
                        IsSafe = !GitRunHelpers.IsPullRequestRef(data.Ref),
                        PreviousCommit = data.Before,
                        CommitMessage = data.HeadCommit?.Message ?? data.Commits?.FirstOrDefault()?.Message,
                        ParsedCommits = data.Commits,
                    };

                    await database.CiBuilds.AddAsync(build);
                    await database.SaveChangesAsync();

                    jobClient.Enqueue<CheckAndStartCIBuild>(x =>
                        x.Execute(build.CiProjectId, build.CiBuildId, CancellationToken.None));
                }

                if (!matched)
                    logger.LogWarning("Push event didn't match any repos: {Fullname}", data.Repository.FullName);
            }
        }
        else if (!string.IsNullOrEmpty(data.Ref))
        {
            // This is a branch push (or maybe a tag?)
        }
        else if (type == "pull_request")
        {
            bool matched = false;

            if (data.Repository == null)
            {
                throw new HttpResponseException
                {
                    Value = new BasicJSONErrorResult("Invalid request",
                        "Repository is needed for this event type").ToString(),
                };
            }

            if (data.PullRequest == null)
            {
                throw new HttpResponseException
                {
                    Value = new BasicJSONErrorResult("Invalid request",
                        "PullRequest data is needed for this event type").ToString(),
                };
            }

            jobClient.Enqueue<CheckPullRequestStatusJob>(x => x.Execute(data.Repository.FullName,
                data.PullRequest.Number, data.PullRequest.Head.Sha, data.PullRequest.User.Login,
                !data.IsClosedPullRequest, CancellationToken.None));

            // Detect if this PR is for any of our repos
            foreach (var project in await database.CiProjects.Where(p =>
                         p.ProjectType == CIProjectType.Github && p.Enabled && !p.Deleted &&
                         p.RepositoryFullName == data.Repository.FullName).ToListAsync())
            {
                matched = true;

                if (data.IsClosedPullRequest)
                {
                    logger.LogInformation("A pull request was closed");
                }
                else if (data.Action is "synchronize" or "opened")
                {
                    // PR content was changed so we should rebuild (we don't react to other actions to avoid
                    // duplicate builds)

                    // TODO: CLA checks for PRs
                    // Queue a CLA check

                    // Only non-primary repo PRs have CI jobs ran on them as main repo commits trigger
                    // the push event
                    if (data.PullRequest.Head.Repo.Id != data.Repository.Id)
                    {
                        logger.LogInformation("Received pull request event from a fork: {FullName}",
                            data.PullRequest.Head.Repo.FullName);

                        var headRef = GitRunHelpers.GenerateRefForPullRequest(data.PullRequest.Number);

                        // Detect next id
                        var previousBuildId = await database.CiBuilds.Where(b => b.CiProjectId == project.Id)
                            .MaxAsync(b => (long?)b.CiBuildId) ?? 0;

                        var build = new CiBuild
                        {
                            CiProjectId = project.Id,
                            CiBuildId = ++previousBuildId,
                            CommitHash = data.PullRequest.Head.Sha,
                            RemoteRef = headRef,
                            Branch = GitRunHelpers.ParseRefBranch(headRef),
                            IsSafe = false,
                            PreviousCommit = data.PullRequest.Base.Sha,
                            CommitMessage = $"Pull request #{data.PullRequest.Number}",

                            // TODO: commits would need to be retrieved from data.PullRequest.CommitsUrl
                            Commits = null,
                        };

                        await database.CiBuilds.AddAsync(build);
                        await database.SaveChangesAsync();

                        jobClient.Enqueue<CheckAndStartCIBuild>(x =>
                            x.Execute(build.CiProjectId, build.CiBuildId, CancellationToken.None));
                    }

                    // TODO: could run some special actions on PR open
                    if (data.Action == "opened")
                    {
                    }
                }
            }

            if (!matched)
            {
                logger.LogWarning("Pull request event didn't match any repos: {Fullname}",
                    data.Repository.FullName);
            }
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
            throw new HttpResponseException
            {
                Value = new BasicJSONErrorResult("Invalid request", "Missing X-Hub-Signature-256 header").ToString(),
            };
        }

        var actualSignature = header[0];

        var readBody = await Request.ReadBodyAsync();
        var rawPayload = readBody.Buffer.ToArray();

        var neededSignature = "sha256=" + Convert.ToHexString(new HMACSHA256(Encoding.UTF8.GetBytes(hook.Secret))
            .ComputeHash(rawPayload)).ToLowerInvariant();

        if (!SecurityHelpers.SlowEquals(neededSignature, actualSignature))
        {
            logger.LogWarning(
                "Github webhook signature ({ActualSignature}) didn't match expected value ({NeededSignature})",
                actualSignature, neededSignature);
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

public class GithubWebhookContent
{
    public string? Action { get; set; }

    [Required]
    [JsonPropertyName("hook_id")]
    public long HookId { get; set; }

    public long Number { get; set; }

    public bool Merged { get; set; }

    public string? Ref { get; set; }

    [JsonPropertyName("ref_type")]
    public string? RefType { get; set; }

    /// <summary>
    ///   Commit before Ref
    /// </summary>
    public string? Before { get; set; }

    /// <summary>
    ///   Commit on Ref after a push
    /// </summary>
    public string? After { get; set; }

    public List<GithubCommit>? Commits { get; set; }

    [JsonPropertyName("head_commit")]
    public GithubCommit? HeadCommit { get; set; }

    public GithubPusher? Pusher { get; set; }

    [Required]
    public GithubHookInfo Hook { get; set; } = new();

    public GithubRepository? Repository { get; set; }

    public GithubOrganization? Organization { get; set; }

    [Required]
    public GithubUserInfo Sender { get; set; } = new();

    [JsonPropertyName("pull_request")]
    public GithubPullRequest? PullRequest { get; set; }

    public bool Deleted { get; set; }

    [JsonIgnore]
    public bool IsClosedPullRequest => Deleted || PullRequest is { State: "closed" };
}

public class GithubCommit
{
    /// <summary>
    ///   Commit hash
    /// </summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("tree_id")]
    public string? TreeId { get; set; }

    public string? Timestamp { get; set; }

    public string? Message { get; set; }

    public CommitAuthor? Author { get; set; }

    public CommitAuthor? Committer { get; set; }

    // The file change overview this commit has
    public List<string>? Added { get; set; }
    public List<string>? Removed { get; set; }
    public List<string>? Modified { get; set; }
}

public class GithubPullRequest
{
    public string? Url { get; set; }
    public long Id { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [Required]
    public long Number { get; set; }

    [Required]
    public string State { get; set; } = string.Empty;

    public bool Locked { get; set; }
    public bool Merged { get; set; }
    public string? Title { get; set; }

    [Required]
    public GithubUserInfo User { get; set; } = new();

    public string? Body { get; set; }

    public bool Draft { get; set; }

    [JsonPropertyName("commits_url")]
    public string? CommitsUrl { get; set; }

    [JsonPropertyName("comments_url")]
    public string? CommentsUrl { get; set; }

    [JsonPropertyName("statuses_url")]
    public string? StatusesUrl { get; set; }

    [Required]
    public GithubRepoRef Head { get; set; } = new();

    [Required]
    public GithubRepoRef Base { get; set; } = new();

    [JsonPropertyName("author_association")]
    public string? AuthorAssociation { get; set; }
}

public class CommitAuthor
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
}

public class GithubPusher
{
    public string? Name { get; set; }
    public string? Email { get; set; }
}

public class GithubHookInfo
{
    [Required]
    public string Type { get; set; } = string.Empty;

    [Required]
    public long Id { get; set; }
}

public class GithubRepository
{
    [Required]
    public long Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    public bool Private { get; set; }

    public GithubUserInfo? Owner { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    /// <summary>
    ///   The API url
    /// </summary>
    public string? Url { get; set; }

    public bool Fork { get; set; }

    [Required]
    [JsonPropertyName("clone_url")]
    public string CloneUrl { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; set; } = string.Empty;
}

public class GithubOrganization
{
}

public class GithubRepoRef
{
    [Required]
    public string Label { get; set; } = string.Empty;

    [Required]
    public string Ref { get; set; } = string.Empty;

    [Required]
    public string Sha { get; set; } = string.Empty;

    public GithubUserInfo? User { get; set; }

    [Required]
    public GithubRepository Repo { get; set; } = new();
}
