namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Models;

public sealed class PatreonCreatorAPI : IDisposable
{
    private readonly HttpClient client;

    public PatreonCreatorAPI(string patreonToken)
    {
        client = new HttpClient
        {
            DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", patreonToken) },
        };
    }

    public PatreonCreatorAPI(PatreonSettings settings) : this(settings.CreatorToken)
    {
    }

    /// <summary>
    ///   Gets all patrons of the active campaign
    /// </summary>
    /// <param name="settings">Where to get the campaign from</param>
    /// <param name="cancellationToken">Supports canceling this while waiting</param>
    /// <returns>API response with all the patron objects</returns>
    public async Task<List<PatronMemberInfo>> GetPatrons(PatreonSettings settings,
        CancellationToken cancellationToken)
    {
        // ReSharper disable once StringLiteralTypo
        var url =
            $"https://www.patreon.com/api/oauth2/api/campaigns/{settings.CampaignId}/pledges?include=patron.null," +
            "reward&fields%5Bpledge%5D=status,currency,amount_cents,declined_since";

        var result = new List<PatronMemberInfo>();

        while (!string.IsNullOrEmpty(url))
        {
            var response = await client.GetFromJsonAsync<PatreonAPIListResponse>(url, cancellationToken);

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
                    throw new PatreonAPIDataException(
                        "Pledge relationship to reward data is not included for user");
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

    public void Dispose()
    {
        client.Dispose();
    }

    public class PatronMemberInfo
    {
        public PatreonObjectData? Pledge { get; set; }
        public PatreonObjectData? User { get; set; }
        public PatreonObjectData? Reward { get; set; }
    }
}
