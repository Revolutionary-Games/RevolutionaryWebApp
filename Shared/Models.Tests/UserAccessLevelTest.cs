namespace ThriveDevCenter.Shared.Models.Tests
{
    using Xunit;

    public class UserAccessLevelTest
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
            Assert.False(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.NotLoggedIn));
        }
    }
}
