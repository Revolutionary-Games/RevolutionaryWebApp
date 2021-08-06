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
    public class GithubAPI
    {
        private readonly ILogger<GithubAPI> logger;
        private readonly HttpClient client;

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

        public Task<GithubUserInfo> GetCurrentUserInfo()
        {
            if (!CheckIsConfigured())
                return Task.FromResult<GithubUserInfo>(null);

            return client.GetFromJsonAsync<GithubUserInfo>("https://api.github.com/user");
        }

        public Task<List<GithubEmail>> GetCurrentUserEmails()
        {
            if (!CheckIsConfigured())
                return Task.FromResult(new List<GithubEmail>());

            return client.GetFromJsonAsync<List<GithubEmail>>("https://api.github.com/user/emails");
        }

        public async Task<bool> SetCommitStatus(string qualifiedRepoName, string sha, CommitStatus state,
            string buildStatusUrl, string description, string contextSuffix)
        {
            if (!CheckIsConfigured())
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

        protected bool CheckIsConfigured()
        {
            if (Configured)
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

        private class CommitStatusSetRequest
        {
            // ReSharper disable UnusedAutoPropertyAccessor.Local

            [JsonPropertyName("state")]
            public CommitStatus State { get; set; }

            [JsonPropertyName("target_url")]
            public string TargetUrl { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; }

            [JsonPropertyName("context")]
            public string Context { get; set; }

            // ReSharper restore UnusedAutoPropertyAccessor.Local
        }
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

        [JsonPropertyName("node_id")]
        public string NodeId { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        /// <summary>
        ///   Valid values seem to be "User" and "Organization"
        /// </summary>
        public string Type { get; set; } = "User";

        public string Name { get; set; }

        public string Email { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    public class GithubEmail
    {
        [Required]
        public string Email { get; set; }

        public bool Verified { get; set; }

        public bool Primary { get; set; }

        public string Visibility { get; set; } = "private";
    }
}
