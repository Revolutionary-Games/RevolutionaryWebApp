namespace RevolutionaryWebApp.Server.Services;

using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using RevolutionaryWebApp.Shared.Models;

public sealed class PatreonCreatorAPI : IPatreonCreatorAPI
{
    public async Task<List<PatronMemberInfo>> GetPatrons(HttpClient client, string campaignId, string token,
        CancellationToken cancellationToken)
    {
        // ReSharper disable StringLiteralTypo
        var url =
            $"https://www.patreon.com/api/oauth2/api/campaigns/{campaignId}/pledges?include=patron.null," +
            "reward&fields%5Bpledge%5D=status,currency,amount_cents,declined_since";

        // ReSharper restore StringLiteralTypo

        var result = new List<PatronMemberInfo>();

        while (!string.IsNullOrEmpty(url))
        {
            var response = await GetAuthenticated<PatreonAPIListResponse>(client, url, token, cancellationToken);

            if (response == null)
                throw new PatreonAPIDataException("failed to deserialize response from patreon API");

            foreach (var data in response.Data)
            {
                if (data.Type != "pledge")
                    continue;

                var patronRelationship = data.Relationships?.Patron;

                if (patronRelationship?.Data == null)
                    throw new PatreonAPIDataException("Pledge relationship to patron doesn't exist");

                var userData =
                    response.FindIncludedObject(patronRelationship.Data.Id, patronRelationship.Data.Type);

                if (userData == null)
                    throw new PatreonAPIDataException("Failed to find pledge's related user object");

                var rewardRelationship = data.Relationships?.Reward;

                if (rewardRelationship == null)
                {
                    throw new PatreonAPIDataException("Pledge relationship to reward data is not included for user");
                }

                // This happens if the user has not selected a reward
                // TODO: would be nice to log this problem here as we should let the patron know they need to
                // select a reward
                if (rewardRelationship.Data == null)
                    continue;

                var rewardData =
                    response.FindIncludedObject(rewardRelationship.Data.Id, rewardRelationship.Data.Type);

                if (rewardData == null)
                    throw new PatreonAPIDataException("Failed to find pledge's related reward object");

                result.Add(new PatronMemberInfo
                {
                    Pledge = data,
                    User = userData,
                    Reward = rewardData,
                });
            }

            // Pagination
            if (response.Links != null && response.Links.TryGetValue("next", out string? nextUrl))
            {
                url = nextUrl;
            }
            else
            {
                // No more pages time to break the loop
                url = null;
            }
        }

        return result;
    }

    public async Task<PatreonAPIObjectResponse> GetOwnDetails(HttpClient client, string token,
        CancellationToken cancellationToken)
    {
        var response = await GetAuthenticated<PatreonAPIObjectResponse>(client,
            "https://www.patreon.com/api/oauth2/v2/identity", token, cancellationToken);

        if (response == null)
            throw new PatreonAPIDataException("failed to deserialize response from patreon API");

        return response;
    }

    public async Task<List<PatreonObjectData>> GetCampaigns(HttpClient client, string token,
        CancellationToken cancellationToken)
    {
        var response = await GetAuthenticated<PatreonAPIListResponse>(client,
            "https://www.patreon.com/api/oauth2/v2/campaigns", token, cancellationToken);

        if (response == null)
            throw new PatreonAPIDataException("failed to deserialize response from patreon API");

        return response.Data;
    }

    public async Task<List<PatreonObjectData>> GetRewards(HttpClient client, string campaignId, string token,
        CancellationToken cancellationToken)
    {
        // ReSharper disable StringLiteralTypo
        var response = await GetAuthenticated<PatreonAPIListResponse>(client,
            $"https://www.patreon.com/api/oauth2/v2/campaigns/{campaignId}/rewards?fields%5Breward%5D=title",
            token, cancellationToken);

        // ReSharper restore StringLiteralTypo

        if (response == null)
            throw new PatreonAPIDataException("failed to deserialize response from patreon API");

        return response.Data;
    }

    private async Task<T?> GetAuthenticated<T>(HttpClient client, string url, string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }
}
