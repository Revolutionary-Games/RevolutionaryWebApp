namespace RevolutionaryWebApp.Server.Services;

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Shared.Models;

public interface IPatreonCreatorAPI
{
    /// <summary>
    ///   Gets all patrons of the active campaign
    /// </summary>
    /// <param name="client">HttpClient to use</param>
    /// <param name="campaignId">Where to get the campaign from</param>
    /// /// <param name="token">Authorization token to use</param>
    /// <param name="cancellationToken">Supports cancelling this while waiting</param>
    /// <returns>API response with all the patron objects</returns>
    Task<List<PatronMemberInfo>> GetMembers(HttpClient client, string campaignId, string token,
        CancellationToken cancellationToken);

    Task<PatreonAPIObjectResponse> GetIdentity(HttpClient client, string token,
        CancellationToken cancellationToken);

    Task<List<PatreonObjectData>> GetCampaigns(HttpClient client, string token,
        CancellationToken cancellationToken);

    Task<List<PatreonObjectData>> GetTiers(HttpClient client, string campaignId, string token,
        CancellationToken cancellationToken);
}
