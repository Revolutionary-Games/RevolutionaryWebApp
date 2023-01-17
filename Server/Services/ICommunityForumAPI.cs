namespace ThriveDevCenter.Server.Services;

using System.Net.Http;
using Microsoft.Extensions.Configuration;

public interface ICommunityForumAPI : IDiscourseAPI
{
}

public class CommunityForumAPI : DiscourseAPI, ICommunityForumAPI
{
    public CommunityForumAPI(IConfiguration configuration, IHttpClientFactory httpClientFactory) : base(
        configuration["Login:CommunityForum:BaseUrl"],
        configuration["Login:CommunityForum:ApiKey"], httpClientFactory)
    {
    }
}
