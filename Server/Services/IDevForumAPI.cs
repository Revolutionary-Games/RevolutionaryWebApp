namespace RevolutionaryWebApp.Server.Services;

using System.Net.Http;
using Microsoft.Extensions.Configuration;

public interface IDevForumAPI : IDiscourseAPI
{
}

public class DevForumAPI : DiscourseAPI, IDevForumAPI
{
    public DevForumAPI(IConfiguration configuration, IHttpClientFactory clientFactory) : base(
        configuration["Login:DevForum:BaseUrl"],
        configuration["Login:DevForum:ApiKey"], clientFactory)
    {
    }
}
