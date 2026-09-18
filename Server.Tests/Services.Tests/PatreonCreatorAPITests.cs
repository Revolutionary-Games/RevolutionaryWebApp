namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Server.Services;
using Xunit;

public sealed class PatreonCreatorAPITests
{
    private const string TestData = """
                                    {
                                      "data": [{
                                        "type": "member",
                                        "id": "member-1",
                                        "attributes": {
                                          "email": "patron@example.com",
                                          "full_name": "Patron",
                                          "patron_status": "active_patron",
                                          "currently_entitled_amount_cents": 500
                                        },
                                        "relationships": {
                                          "user": { "data": { "type": "user", "id": "user-1" } },
                                          "currently_entitled_tiers": { "data": [{ "type": "tier", "id": "tier-1" }] }
                                        }
                                      }],
                                      "included": [
                                        { "type": "user", "id": "user-1", "attributes": { "full_name": "Patron" } },
                                        { "type": "tier", "id": "tier-1", "attributes": 
                                        { "title": "Dev Builds", "amount_cents": 500 } }
                                      ]
                                    }
                                    """;

    [Fact]
    public async Task GetMembers_UsesV2MembersEndpointAndMapsTiers()
    {
        var handler = new RecordingHandler(TestData);
        var api = new PatreonCreatorAPI();

        var result = await api.GetMembers(new HttpClient(handler), "campaign-1", "token", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("member-1", result[0].Member!.Id);
        Assert.Equal("user-1", result[0].User!.Id);
        Assert.Single(result[0].EntitledTiers);
        Assert.Equal("tier-1", result[0].EntitledTiers[0].Id);
        Assert.StartsWith("https://www.patreon.com/api/oauth2/v2/campaigns/campaign-1/members", handler.Url);
        Assert.Contains("include=currently_entitled_tiers,user", handler.Url);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public string Url { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            });
        }
    }
}
