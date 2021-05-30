namespace ThriveDevCenter.Server.Services
{
    using System;
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
            [JsonPropertyName("state")]
            public CommitStatus State { get; set; }

            [JsonPropertyName("target_url")]
            public string TargetUrl { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; }

            [JsonPropertyName("context")]
            public string Context { get; set; }
        }
    }
}
