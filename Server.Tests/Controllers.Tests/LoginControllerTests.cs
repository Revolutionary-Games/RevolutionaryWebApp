namespace ThriveDevCenter.Server.Tests.Controllers.Tests;

using Microsoft.Extensions.Primitives;
using Server.Utilities;
using Xunit;

public class LoginControllerTests
{
    [Fact]
    public void LoginController_DiscourseGroupMembershipCheckWorks()
    {
        StringValues groups = new StringValues(new[]
        {
            "Supporter", "Developer", "VIP_supporter", "trust_level_0", "trust_level_2", "trust_level_3",
            "trust_level_1",
        });

        Assert.Contains(groups, group =>
            DiscourseApiHelpers.CommunityDevBuildGroup.Equals(group) ||
            DiscourseApiHelpers.CommunityVIPGroup.Equals(group));

        var blockString =
            "Supporter,Developer,VIP_supporter,trust_level_0,trust_level_2,trust_level_3,trust_level_1";

        var parsedGroups = blockString.Split(',');

        Assert.Contains(parsedGroups, group =>
            DiscourseApiHelpers.CommunityDevBuildGroup.Equals(group) ||
            DiscourseApiHelpers.CommunityVIPGroup.Equals(group));
    }
}
