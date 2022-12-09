namespace ThriveDevCenter.Server.Services;

using System.Net.Http;
using Microsoft.Extensions.Configuration;

public class DevForumAPI : DiscourseAPI
{
    public DevForumAPI(IConfiguration configuration, IHttpClientFactory clientFactory) : base(
        configuration["Login:DevForum:BaseUrl"],
        configuration["Login:DevForum:ApiKey"], clientFactory)
    {
    }
}
