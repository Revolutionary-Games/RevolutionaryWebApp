namespace ThriveDevCenter.Shared.Tests.Models.Tests
{
    using Shared.Models;
    using Xunit;

    public class UserAccessLevelTests
    {
        [Fact]
        public void UserAccessLevel_AdminValuesWork()
        {
            Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.Admin));
            Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.Developer));
            Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.User));
            Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.NotLoggedIn));
        }

        [Fact]
        public void UserAccessLevel_DeveloperValuesWork()
        {
            Assert.False(UserAccessLevel.Developer.HasAccess(UserAccessLevel.Admin));
            Assert.True(UserAccessLevel.Developer.HasAccess(UserAccessLevel.Developer));
            Assert.True(UserAccessLevel.Developer.HasAccess(UserAccessLevel.User));
            Assert.True(UserAccessLevel.Developer.HasAccess(UserAccessLevel.NotLoggedIn));
        }

        [Fact]
        public void UserAccessLevel_UserValuesWork()
        {
            Assert.False(UserAccessLevel.User.HasAccess(UserAccessLevel.Admin));
            Assert.False(UserAccessLevel.User.HasAccess(UserAccessLevel.Developer));
            Assert.True(UserAccessLevel.User.HasAccess(UserAccessLevel.User));
            Assert.True(UserAccessLevel.User.HasAccess(UserAccessLevel.NotLoggedIn));
        }

        [Fact]
        public void UserAccessLevel_NotLoggedInValuesWork()
        {
            Assert.False(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.Admin));
            Assert.False(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.Developer));
            Assert.False(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.User));
            Assert.True(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.NotLoggedIn));
        }
    }
}
