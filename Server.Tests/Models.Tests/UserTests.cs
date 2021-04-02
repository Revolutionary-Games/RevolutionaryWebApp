namespace ThriveDevCenter.Server.Tests.Models.Tests
{
    using System.Threading.Tasks;
    using Fixtures;
    using Microsoft.EntityFrameworkCore;
    using Server.Authorization;
    using Server.Models;
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
    }
}
