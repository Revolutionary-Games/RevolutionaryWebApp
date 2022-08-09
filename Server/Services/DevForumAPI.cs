namespace ThriveDevCenter.Server.Services;

using Microsoft.Extensions.Configuration;

public class DevForumAPI : DiscourseAPI
{
    public DevForumAPI(IConfiguration configuration) : base(configuration["Login:DevForum:BaseUrl"],
        configuration["Login:DevForum:ApiKey"])
    {
    }
}