namespace ThriveDevCenter.Server.Services;

using Microsoft.Extensions.Configuration;

public class CommunityForumAPI : DiscourseAPI
{
    public CommunityForumAPI(IConfiguration configuration) : base(configuration["Login:CommunityForum:BaseUrl"],
        configuration["Login:CommunityForum:ApiKey"])
    {
    }
}