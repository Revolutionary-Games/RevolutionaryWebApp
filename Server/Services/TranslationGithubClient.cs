namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Shared;

/// <summary>
///   Small GitHub API surface used by translation publishing.
/// </summary>
public sealed class TranslationGithubClient : ITranslationGithubClient
{
    private const string GithubApiVersion = "2026-03-10";

    private readonly HttpClient client;

    public TranslationGithubClient(string token)
    {
        client = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ThriveDevCenter",
            $"{AppInfo.Major}.{AppInfo.Minor}"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", GithubApiVersion);
    }

    public async Task<string> GetLogin(CancellationToken cancellationToken)
    {
        using var user = await Get("user", cancellationToken);
        return user.RootElement.GetProperty("login").GetString() ??
            throw new InvalidOperationException("GitHub did not return the publishing account login");
    }

    public async Task EnsureFork(string sourceRepository, string owner, CancellationToken cancellationToken)
    {
        var repo = sourceRepository[(sourceRepository.IndexOf('/') + 1)..];
        var forkPath = $"repos/{owner}/{repo}";
        using var current = await client.GetAsync(forkPath, cancellationToken);
        if (current.IsSuccessStatusCode)
        {
            await ValidateFork(current, sourceRepository, cancellationToken);
            return;
        }

        if (current.StatusCode != HttpStatusCode.NotFound)
            throw new HttpRequestException($"GitHub fork lookup failed: {(int)current.StatusCode}");

        using var response = await client.PostAsJsonAsync($"repos/{sourceRepository}/forks", new { },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub fork creation failed: {(int)response.StatusCode}");

        for (var attempt = 0; attempt < 12; ++attempt)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            using var ready = await client.GetAsync(forkPath, cancellationToken);
            if (ready.IsSuccessStatusCode)
            {
                await ValidateFork(ready, sourceRepository, cancellationToken);
                return;
            }

            if (ready.StatusCode != HttpStatusCode.NotFound)
                throw new HttpRequestException($"GitHub fork lookup failed: {(int)ready.StatusCode}");
        }

        throw new TimeoutException("GitHub did not make the fork available in time");
    }

    public async Task<TranslationGithubPullRequest?> GetPullRequest(string repository, int number,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"repos/{repository}/pulls/{number}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        return ReadPullRequest(document?.RootElement ?? throw new InvalidOperationException("Empty PR response"));
    }

    public async Task<TranslationGithubPullRequest> CreatePullRequest(string repository, string branch,
        string forkOwner, string headBranch, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync($"repos/{repository}/pulls", new
        {
            title = "Update translations",
            body = "Translations maintained in Thrive DevCenter.",
            head = $"{forkOwner}:{headBranch}",
            @base = branch,
            maintainer_can_modify = false,
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        return ReadPullRequest(document?.RootElement ?? throw new InvalidOperationException("Empty PR response"));
    }

    public void Dispose()
    {
        client.Dispose();
    }

    private static TranslationGithubPullRequest ReadPullRequest(JsonElement element)
    {
        var head = element.GetProperty("head");
        var headOwner = head.TryGetProperty("repo", out var repository) &&
            repository.ValueKind == JsonValueKind.Object && repository.TryGetProperty("owner", out var owner) &&
            owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty("login", out var login) ?
                login.GetString() ?? string.Empty :
                string.Empty;
        var mergeable = element.TryGetProperty("mergeable", out var mergeableProperty) &&
            mergeableProperty.ValueKind is JsonValueKind.True or JsonValueKind.False ?
                mergeableProperty.GetBoolean() :
                (bool?)null;
        return new TranslationGithubPullRequest(element.GetProperty("number").GetInt32(),
            element.GetProperty("state").GetString() == "open", head.GetProperty("sha").GetString() ?? string.Empty,
            head.GetProperty("ref").GetString() ?? string.Empty, headOwner,
            element.GetProperty("base").GetProperty("ref").GetString() ?? string.Empty, mergeable);
    }

    private static async Task ValidateFork(HttpResponseMessage response, string sourceRepository,
        CancellationToken cancellationToken)
    {
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var root = document?.RootElement ?? throw new InvalidOperationException("Empty fork response");
        var parentName = root.TryGetProperty("parent", out var parent) &&
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty("full_name", out var fullName) ?
                fullName.GetString() :
                null;
        if (!string.Equals(parentName, sourceRepository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Publishing account has an unrelated repository with this name");
    }

    private async Task<JsonDocument> Get(string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken) ??
            throw new InvalidOperationException("Empty GitHub API response");
    }
}

public sealed record TranslationGithubPullRequest(int Number, bool Open, string HeadSha, string HeadBranch,
    string HeadOwner, string BaseBranch, bool? Mergeable);
