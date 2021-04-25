namespace ThriveDevCenter.Server.Tests.Models.Tests
{
    using System;
    using System.Threading.Tasks;
    using Fixtures;
    using Microsoft.EntityFrameworkCore;
    using Server.Authorization;
    using Server.Models;
    using Server.Utilities;
    using Xunit;

    public class UserTests
    {
        [Fact]
        public async Task User_ClearingLfsTokenClearsHashedAsWell()
        {
            var database = new EditableInMemoryDatabaseFixture("UserClearApiToken");

            var user = new User()
            {
                UserName = "test",
                Email = "test@example.com",
                LfsToken = NonceGenerator.GenerateNonce(32)
            };

            await database.Database.Users.AddAsync(user);
            await database.Database.SaveChangesAsync();

            Assert.NotNull(user.LfsToken);
            Assert.NotNull(user.HashedLfsToken);

            user.LfsToken = null;
            await database.Database.SaveChangesAsync();

            Assert.Null(user.LfsToken);
            Assert.Null(user.HashedLfsToken);

            var searched = await database.Database.Users.FirstAsync();

            Assert.Equal(user.Id, searched.Id);
            Assert.Null(searched.HashedLfsToken);
        }

        [Fact]
        public async Task User_RecreatingLinkCodeUpdatesHashedVersion()
        {
            var database = new EditableInMemoryDatabaseFixture("UserLinkCodeRecreated");

            var user = new User()
            {
                UserName = "test",
                Email = "test@example.com",
            };

            await database.Database.Users.AddAsync(user);
            await database.Database.SaveChangesAsync();

            Assert.Null(user.LauncherLinkCode);
            Assert.Null(user.HashedLauncherLinkCode);

            var firstGuid = Guid.NewGuid().ToString();

            user.LauncherLinkCode = firstGuid;
            await database.Database.SaveChangesAsync();

            Assert.NotNull(user.LauncherLinkCode);
            Assert.NotNull(user.HashedLauncherLinkCode);
            Assert.Equal(SelectByHashedProperty.HashForDatabaseValue(firstGuid), user.HashedLauncherLinkCode);

            // Setting a second code
            var secondGuid = Guid.NewGuid().ToString();
            Assert.NotEqual(secondGuid, firstGuid);

            user.LauncherLinkCode = secondGuid;
            await database.Database.SaveChangesAsync();

            Assert.Equal(SelectByHashedProperty.HashForDatabaseValue(secondGuid), user.HashedLauncherLinkCode);

            // Clearing the code
            user.LauncherLinkCode = null;

            await database.Database.SaveChangesAsync();

            Assert.Null(user.LauncherLinkCode);
            Assert.Null(user.HashedLauncherLinkCode);
        }
    }
}
