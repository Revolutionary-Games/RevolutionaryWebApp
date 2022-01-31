namespace ThriveDevCenter.Server.Services
{
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
    using Shared.Utilities;

    /// <summary>
    ///   Wrapper around the Github HTTP API
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     All qualified repo name parameters assume the repo name in form "username/repoName"
    ///   </para>
    /// </remarks>
    public class GithubAPI : IGithubAPI
    {
        private readonly ILogger<GithubAPI> logger;
        private readonly HttpClient? client;

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
            Success
        }

        public GithubAPI(ILogger<GithubAPI> logger, string oauthToken)
        {
            this.logger = logger;
            if (string.IsNullOrEmpty(oauthToken))
            {
                Configured = false;
                return;
            }

            client = new HttpClient()
            {
                DefaultRequestHeaders =
                {
                    Authorization = new AuthenticationHeaderValue("token", oauthToken),
                    Accept = { new MediaTypeWithQualityHeaderValue(AppInfo.GithubApiContentType) },
                    UserAgent = { new ProductInfoHeaderValue("ThriveDevCenter", $"{AppInfo.Major}.{AppInfo.Minor}") }
                }
            };

            Configured = true;
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
                new CommitStatusSetRequest()
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

    public interface IGithubAPI
    {
        bool Configured { get; }
        bool ThrowIfNotConfigured { get; set; }
        Task<GithubUserInfo?> GetCurrentUserInfo();
        Task<List<GithubEmail>> GetCurrentUserEmails();

        Task<bool> SetCommitStatus(string qualifiedRepoName, string sha, GithubAPI.CommitStatus state,
            string buildStatusUrl, string description, string contextSuffix);

        Task<bool> PostComment(string qualifiedRepoName, long issueOrPullRequestNumber,
            string commentContent);
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
}
