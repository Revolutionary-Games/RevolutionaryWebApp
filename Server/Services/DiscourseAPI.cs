namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using SharedBase.Utilities;
using Utilities;

public class DiscourseAPI : IDiscourseAPI
{
    private const int DefaultRetriesForTooManyRequestsError = 9;

    // TODO: if ever any group / other list requests have more data than this, we need to implement paging
    private const int DiscourseQueryLimit = 1000;

    private readonly Uri? apiBaseUrl;
    private readonly string key;
    private readonly string apiUsername;
    private readonly HttpClient httpClient;

    public DiscourseAPI(string? apiBaseUrl, string? key, IHttpClientFactory clientFactory,
        string apiUsername = "system")
    {
        this.apiBaseUrl = string.IsNullOrEmpty(apiBaseUrl) ? null : new Uri(apiBaseUrl);
        this.key = key ?? string.Empty;
        this.apiUsername = apiUsername;

        httpClient = clientFactory.CreateClient("discourse");

        if (Configured)
        {
            AddDiscourseAccessHeaders(httpClient.DefaultRequestHeaders);
        }
    }

    public bool Configured => apiBaseUrl != null && !string.IsNullOrEmpty(key);

    public async Task<DiscourseGroupMembers> GetGroupMembers(string name, CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        long offset = 0;
        long limit = DiscourseQueryLimit;

        var url = QueryHelpers.AddQueryString(new Uri(apiBaseUrl!, $"groups/{name}/members.json").ToString(),
            new Dictionary<string, string?>
            {
                { "offset", offset.ToString() },
                { "limit", limit.ToString() },
            });

        return await PerformWithRateLimitRetries(
            async () => await httpClient.GetFromJsonAsync<DiscourseGroupMembers>(url, cancellationToken) ??
                throw new JsonException(),
            cancellationToken);
    }

    public async Task<DiscourseUser?> FindUserByEmail(string email, CancellationToken cancellationToken,
        bool includeNonActive = false, bool avoidEmailQuery = true)
    {
        ThrowIfNotConfigured();

        if (string.IsNullOrEmpty(email))
            return null;

        // All used to be the default search but it's probably better to just get the active users whenever needed?
        var searchType = "active";

        if (includeNonActive)
            searchType = "all";

        var url = QueryHelpers.AddQueryString(
            new Uri(apiBaseUrl!, $"admin/users/list/{searchType}.json").ToString(),
            new Dictionary<string, string?>
            {
                { "email", email },
                { "show_emails", avoidEmailQuery ? "false" : "true" },
            });

        return await PerformWithRateLimitRetries(
            async () =>
            {
                var list = await httpClient.GetFromJsonAsync<List<DiscourseUser>>(url, cancellationToken);
                if (list == null)
                    throw new Exception("Discourse didn't return a list");

                if (avoidEmailQuery)
                {
                    if (list.Count > 1)
                    {
                        throw new Exception(
                            "Multiple users found with email, can't differentiate due to avoidEmailQuery");
                    }

                    if (list.Count > 0)
                    {
                        // Assume returned user matched exactly the email. This avoids requesting the email
                        // data from discourse
                        list[0].Email = email;
                    }
                }

                return list.FirstOrDefault();
            }, cancellationToken);
    }

    public async Task<DiscourseSingleUserInfo> UserInfoByName(string username, CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        var url = new Uri(apiBaseUrl!, $"u/{username}.json").ToString();

        return await PerformWithRateLimitRetries(
            async () => await httpClient.GetFromJsonAsync<DiscourseSingleUserInfo>(url, cancellationToken) ??
                throw new JsonException(),
            cancellationToken);
    }

    public async Task<DiscourseGroup> GetGroupInfo(string name, CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        var url = new Uri(apiBaseUrl!, $"groups/{name}.json").ToString();

        return await PerformWithRateLimitRetries(
            async () => (await httpClient.GetFromJsonAsync<DiscourseGroupInfoResponse>(url, cancellationToken))
                ?.Group ?? throw new NullDecodedJsonException(),
            cancellationToken);
    }

    public async Task AddGroupMembers(DiscourseGroup group, IEnumerable<string> usernames,
        CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        var payload = new DiscourseGroupMemberRequest(usernames);
        if (string.IsNullOrEmpty(payload.Usernames))
            return;

        var url = GroupMemberChangeURL(group);

        await PerformWithRateLimitRetries(
            async () => await httpClient.PutAsJsonAsync(url, payload, cancellationToken),
            cancellationToken);
    }

    public async Task RemoveGroupMembers(DiscourseGroup group, IEnumerable<string> usernames,
        CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        var payload = new DiscourseGroupMemberRequest(usernames);
        if (string.IsNullOrEmpty(payload.Usernames))
            return;

        var url = GroupMemberChangeURL(group);

        await PerformWithRateLimitRetries(
            async () => await httpClient.DeleteAsJsonAsync(url, payload, cancellationToken),
            cancellationToken);
    }

    protected async Task<TResult> PerformWithRateLimitRetries<TResult>(Func<Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        Exception? latestException = null;

        for (int i = 0; i < DefaultRetriesForTooManyRequestsError; ++i)
        {
            try
            {
                var task = operation.Invoke();
                return await task;
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    // We only handle retries for too many request
                    // TODO: should also internal server errors / gateway errors be retried?
                    throw;
                }

                latestException = e;

                // TODO: there used to be logging here

                await Task.Delay(TimeSpan.FromSeconds(i + (i + 1) * 4), cancellationToken);
            }
        }

        throw new Exception("Discourse API request ran out of retries", latestException);
    }

    protected void AddDiscourseAccessHeaders(HttpHeaders headers)
    {
        headers.Add("Api-Key", key);
        headers.Add("Api-Username", apiUsername);
    }

    protected void ThrowIfNotConfigured()
    {
        if (!Configured)
            throw new InvalidOperationException("DiscourseAPI instance is not configured");
    }

    private string GroupMemberChangeURL(DiscourseGroup group)
    {
        ThrowIfNotConfigured();
        return new Uri(apiBaseUrl!, $"groups/{group.Id}/members.json").ToString();
    }
}

public class DiscourseGroupMembers
{
    [Required]
    public List<DiscourseUser> Owners { get; set; } = new();

    [Required]
    public List<DiscourseUser> Members { get; set; } = new();

    public DiscourseResponseMeta? Meta { get; set; }

    /// <summary>
    ///   Finds group members that need to be removed (weren't used when checking patrons)
    /// </summary>
    public IEnumerable<DiscourseUser> GetUnmarkedMembers()
    {
        foreach (var user in Members)
        {
            if (user.Marked)
                continue;

            if (Owners.Any(owner => owner.Id == user.Id))
                continue;

            yield return user;
        }
    }

    public bool CheckMemberShipAndMark(string username)
    {
        foreach (var user in Members)
        {
            if (user.Username == username)
            {
                user.Marked = true;
                return true;
            }
        }

        return false;
    }

    public bool IsOwner(string username)
    {
        foreach (var user in Owners)
        {
            if (user.Username == username)
                return true;
        }

        return false;
    }
}

public class DiscourseGroup
{
    [Required]
    public long Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///   Display name of the group. Not set when getting the actual group data. Instead FullName is included
    /// </summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }
}

public class DiscourseUser
{
    [Required]
    public long Id { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    public string? Name { get; set; }

    public bool Moderator { get; set; }

    public bool Admin { get; set; }

    public string? Email { get; set; }

    public List<DiscourseGroup> Groups { get; set; } = new();

    // There's way more properties that aren't parsed:
    // https://docs.discourse.org/#tag/Users/paths/~1u~1{username}.json/get

    [JsonIgnore]
    public bool Marked { get; set; }

    public bool IsInGroup(string groupName)
    {
        return Groups.Any(g => g.Name == groupName);
    }
}

public class DiscourseSingleUserInfo
{
    [Required]
    public DiscourseUser User { get; set; } = new();

    [JsonPropertyName("user_badges")]
    public List<DiscourseGrantedBadge>? UserBadges { get; set; }
}

public class DiscourseGrantedBadge
{
    // TODO: find out the valid properties
}

public class DiscourseGroupInfoResponse
{
    [Required]
    public DiscourseGroup Group { get; set; } = new();
}

public class DiscourseGroupMemberRequest
{
#pragma warning disable CS8618 //initialized in a method always called in the constructor
    public DiscourseGroupMemberRequest(IEnumerable<string> usernames)
#pragma warning restore CS8618
    {
        UsernamesFromList(usernames);
    }

    /// <summary>
    ///   The usernames to work on. Needs to be in format "username1,username2"
    /// </summary>
    [JsonPropertyName("usernames")]
    public string Usernames { get; set; }

    public void UsernamesFromList(IEnumerable<string> data)
    {
        Usernames = string.Join(',', data);
    }
}

/// <summary>
///   Meta information provided in discourse requests, used for pagination
/// </summary>
public class DiscourseResponseMeta
{
    public long Total { get; set; }
    public long Limit { get; set; }
    public long Offset { get; set; }
}
