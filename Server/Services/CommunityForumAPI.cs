namespace ThriveDevCenter.Server.Services;

using System.Net.Http;
using Microsoft.Extensions.Configuration;

public class CommunityForumAPI : DiscourseAPI
{
    public CommunityForumAPI(IConfiguration configuration, IHttpClientFactory httpClientFactory) : base(
        configuration["Login:CommunityForum:BaseUrl"],
        configuration["Login:CommunityForum:ApiKey"], httpClientFactory)
    {
    }
}
