namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using RevolutionaryWebApp.Shared.Models;

public sealed class PatreonCreatorAPI : IPatreonCreatorAPI
{
    public async Task<List<PatronMemberInfo>> GetMembers(HttpClient client, string campaignId, string token,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://www.patreon.com/api/oauth2/v2/campaigns/{campaignId}/members" +
            "?include=currently_entitled_tiers,user" +
            "&fields%5Bmember%5D=email,full_name,patron_status,currently_entitled_amount_cents," +
            "last_charge_status" +
            "&fields%5Btier%5D=title,amount_cents" +
            "&fields%5Buser%5D=email,first_name,full_name,vanity";

        var result = new List<PatronMemberInfo>();

        while (!string.IsNullOrEmpty(url))
        {
            var response = await GetAuthenticated<PatreonAPIListResponse>(client, url, token, cancellationToken);

            if (response == null)
                throw new PatreonAPIDataException("failed to deserialize response from patreon API");

            foreach (var data in response.Data)
            {
                if (data.Type != "member")
                    continue;

                var userRelationship = data.Relationships?.User;

                if (userRelationship?.Data == null)
                    throw new PatreonAPIDataException("Member relationship to user doesn't exist");

                var userData =
                    response.FindIncludedObject(userRelationship.Data.Id, userRelationship.Data.Type);

                if (userData == null)
                    throw new PatreonAPIDataException("Failed to find member's related user object");

                result.Add(new PatronMemberInfo
                {
                    Member = data,
                    User = userData,
                    EntitledTiers = data.Relationships?.CurrentlyEntitledTiers?.Data
                        .Select(tier => response.FindIncludedObject(tier.Id, "tier"))
                        .Where(tier => tier != null)
                        .Cast<PatreonObjectData>()
                        .ToList() ?? new List<PatreonObjectData>(),
                });
            }

            // Pagination
            if (response.Links != null && response.Links.TryGetValue("next", out string? nextUrl) &&
                !string.IsNullOrEmpty(nextUrl))
            {
                url = nextUrl;
            }
            else if (!string.IsNullOrEmpty(response.Meta.Pagination?.Cursors.Next))
            {
                url = $"https://www.patreon.com/api/oauth2/v2/campaigns/{campaignId}/members" +
                    $"?page%5Bcursor%5D={Uri.EscapeDataString(response.Meta.Pagination.Cursors.Next)}" +
                    "&include=currently_entitled_tiers,user" +
                    "&fields%5Bmember%5D=email,full_name,patron_status," +
                    "currently_entitled_amount_cents,last_charge_status" +
                    "&fields%5Btier%5D=title,amount_cents" +
                    "&fields%5Buser%5D=email,first_name,full_name,vanity";
            }
            else
            {
                // No more pages time to break the loop
                url = null;
            }
        }

        return result;
    }

    public async Task<PatreonAPIObjectResponse> GetIdentity(HttpClient client, string token,
        CancellationToken cancellationToken)
    {
        var response = await GetAuthenticated<PatreonAPIObjectResponse>(client,
            "https://www.patreon.com/api/oauth2/v2/identity?fields%5Buser%5D=email,full_name,vanity,url",
            token, cancellationToken);

        if (response == null)
            throw new PatreonAPIDataException("failed to deserialize response from patreon API");

        return response;
    }

    public async Task<List<PatreonObjectData>> GetCampaigns(HttpClient client, string token,
        CancellationToken cancellationToken)
    {
        var response = await GetAuthenticated<PatreonAPIListResponse>(client,
            "https://www.patreon.com/api/oauth2/v2/campaigns?include=tiers" +
            "&fields%5Bcampaign%5D=name,vanity,url" +
            "&fields%5Btier%5D=title,amount_cents",
            token, cancellationToken);

        if (response == null)
            throw new PatreonAPIDataException("failed to deserialize response from patreon API");

        return response.Data;
    }

    public async Task<List<PatreonObjectData>> GetTiers(HttpClient client, string campaignId, string token,
        CancellationToken cancellationToken)
    {
        var response = await GetAuthenticated<PatreonAPIObjectResponse>(client,
            $"https://www.patreon.com/api/oauth2/v2/campaigns/{campaignId}?include=tiers" +
            "&fields%5Bcampaign%5D=name,vanity,url" +
            "&fields%5Btier%5D=title,amount_cents", token, cancellationToken);

        if (response == null)
            throw new PatreonAPIDataException("failed to deserialize response from patreon API");

        return response.Data.Relationships?.Tiers?.Data
            .Select(tier => response.FindIncludedObject(tier.Id, "tier"))
            .Where(tier => tier != null)
            .Cast<PatreonObjectData>()
            .ToList() ?? response.Included.Where(item => item.Type == "tier").ToList();
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
