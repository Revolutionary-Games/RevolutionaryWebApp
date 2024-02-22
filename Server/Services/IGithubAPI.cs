﻿namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shared;
using SharedBase.Converters;

/// <summary>
///   Wrapper around the Github HTTP API
/// </summary>
/// <remarks>
///   <para>
///     All qualified repo name parameters assume the repo name in form "username/repoName"
///   </para>
/// </remarks>
public interface IGithubAPI : IDisposable
{
    public bool Configured { get; }
    public bool ThrowIfNotConfigured { get; set; }
    public Task<GithubUserInfo?> GetCurrentUserInfo();
    public Task<List<GithubEmail>> GetCurrentUserEmails();

    public Task<bool> SetCommitStatus(string qualifiedRepoName, string sha, GithubAPI.CommitStatus state,
        string buildStatusUrl, string description, string contextSuffix);

    public Task<bool> PostComment(string qualifiedRepoName, long issueOrPullRequestNumber,
        string commentContent);
}

public class GithubAPI : IGithubAPI
{
    private readonly ILogger<GithubAPI> logger;
    private readonly HttpClient? client;

    public GithubAPI(ILogger<GithubAPI> logger, string? oauthToken)
    {
        this.logger = logger;
        if (string.IsNullOrEmpty(oauthToken))
        {
            Configured = false;
            return;
        }

        client = new HttpClient
        {
            DefaultRequestHeaders =
            {
                Authorization = new AuthenticationHeaderValue("token", oauthToken),
                Accept = { new MediaTypeWithQualityHeaderValue(AppInfo.GithubApiContentType) },
                UserAgent = { new ProductInfoHeaderValue("ThriveDevCenter", $"{AppInfo.Major}.{AppInfo.Minor}") },
            },
        };

        Configured = true;
    }

    [JsonConverter(typeof(ActualEnumStringConverter))]
    public enum CommitStatus
    {
        /// <summary>
        ///   Seems like github doesn't recommend ever using this value? But this is said to be a potential value
        ///   so this is included here
        /// </summary>
        [EnumMember(Value = "error")]
        Error,

        [EnumMember(Value = "failure")]
        Failure,

        [EnumMember(Value = "pending")]
        Pending,

        [EnumMember(Value = "success")]
        Success,
    }

    public bool Configured { get; }

    public bool ThrowIfNotConfigured { get; set; }

    public Task<GithubUserInfo?> GetCurrentUserInfo()
    {
        if (!CheckIsConfigured() || client == null)
            return Task.FromResult<GithubUserInfo?>(null);

        return client.GetFromJsonAsync<GithubUserInfo>("https://api.github.com/user");
    }

    public async Task<List<GithubEmail>> GetCurrentUserEmails()
    {
        if (!CheckIsConfigured() || client == null)
            return new List<GithubEmail>();

        var result = await client.GetFromJsonAsync<List<GithubEmail>>("https://api.github.com/user/emails");

        return result ?? new List<GithubEmail>();
    }

    public async Task<bool> SetCommitStatus(string qualifiedRepoName, string sha, CommitStatus state,
        string buildStatusUrl, string description, string contextSuffix)
    {
        if (!CheckIsConfigured() || client == null)
            return false;

        string context = $"DevCenter:{contextSuffix}";

        var response = await client.PostAsJsonAsync(
            $"https://api.github.com/repos/{qualifiedRepoName}/statuses/{sha}",
            new CommitStatusSetRequest
            {
                State = state,
                TargetUrl = buildStatusUrl,
                Description = description,
                Context = context,
            });

        if (!response.IsSuccessStatusCode)
        {
            await ReportFailedRequest(response);
            return false;
        }

        return true;
    }

    public async Task<bool> PostComment(string qualifiedRepoName, long issueOrPullRequestNumber,
        string commentContent)
    {
        if (!CheckIsConfigured() || client == null)
            return false;

        var response = await client.PostAsJsonAsync(
            $"https://api.github.com/repos/{qualifiedRepoName}/issues/{issueOrPullRequestNumber}/comments",
            new CommentPostRequest(commentContent));

        if (!response.IsSuccessStatusCode)
        {
            await ReportFailedRequest(response);
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected bool CheckIsConfigured()
    {
        if (Configured && client != null)
            return true;

        if (ThrowIfNotConfigured)
            throw new Exception("Github API is not configured, can't perform operation");

        logger.LogWarning("Github API is not configured, skipping performing an operation");

        return false;
    }

    protected async Task ReportFailedRequest(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        logger.LogError("Failed to access Github API {ReasonPhrase} (code: {StatusCode}): {Content}",
            response.StatusCode, response.ReasonPhrase, content);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            client?.Dispose();
        }
    }

    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private class CommitStatusSetRequest
    {
        [JsonPropertyName("state")]
        public CommitStatus State { get; set; }

        [Required]
        [JsonPropertyName("target_url")]
        public string TargetUrl { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("context")]
        public string Context { get; set; } = string.Empty;
    }

    private class CommentPostRequest
    {
        public CommentPostRequest(string body)
        {
            Body = body;
        }

        /// <summary>
        ///   The body of the comment
        /// </summary>
        [JsonPropertyName("body")]
        public string Body { get; set; }
    }

    // ReSharper restore UnusedAutoPropertyAccessor.Local
}

public class GithubUserInfo
{
    /// <summary>
    ///   This is the username
    /// </summary>
    [Required]
    public string Login { get; set; } = string.Empty;

    [Required]
    public long Id { get; set; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    /// <summary>
    ///   Valid values seem to be "User" and "Organization"
    /// </summary>
    public string Type { get; set; } = "User";

    public string? Name { get; set; }

    public string? Email { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class GithubEmail
{
    [Required]
    public string Email { get; set; } = string.Empty;

    public bool Verified { get; set; }

    public bool Primary { get; set; }

    public string Visibility { get; set; } = "private";
}

public class GithubMilestone
{
    [Required]
    public long Id { get; set; }

    [Required]
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [Required]
    public string State { get; set; } = "open";

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public GithubUserInfo? Creator { get; set; }

    [JsonPropertyName("open_issues")]
    public long OpenIssues { get; set; }

    [JsonPropertyName("closed_issues")]
    public long ClosedIssues { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("due_on")]
    public DateTime? DueOn { get; set; }
}

public class GithubAsset
{
    [Required]
    public long Id { get; set; }

    [Required]
    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Label { get; set; } = string.Empty;

    [Required]
    public string State { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;

    [Required]
    public long Size { get; set; }

    [Required]
    [JsonPropertyName("download_count")]
    public long DownloadCount { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    public GithubUserInfo? Uploader { get; set; }
}

public class GithubRelease
{
    [Required]
    public long Id { get; set; }

    [Required]
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("assets_url")]
    public string AssetsUrl { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "open";

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public bool Draft { get; set; }
    public bool Prerelease { get; set; }

    public GithubUserInfo? Author { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [Required]
    public List<GithubAsset> Assets { get; set; } = new();
}
