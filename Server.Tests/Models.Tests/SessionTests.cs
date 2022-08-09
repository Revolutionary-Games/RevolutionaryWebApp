namespace ThriveDevCenter.Server.Tests.Models.Tests
{
    using System;
    using System.Threading.Tasks;
    using Fixtures;
    using Microsoft.EntityFrameworkCore;
    using Server.Models;
    using Server.Utilities;
    using Xunit;

    public class SessionTests
    {
        [Fact]
        public async Task Session_AutomaticallyCreatesHashedIdOnSave()
        {
            var database = new EditableInMemoryDatabaseFixture("SessionCreateNewHash");

            var session = new Session()
            {
                SsoNonce = "1234",
            };

            await database.Database.Sessions.AddAsync(session);
            await database.Database.SaveChangesAsync();

            Assert.NotNull(session.HashedId);
            Assert.Equal(SelectByHashedProperty.HashForDatabaseValue(session.Id.ToString()), session.HashedId);

            var searched = await database.Database.Sessions.FirstAsync();

            Assert.Equal(session.Id, searched.Id);
            Assert.Equal(session.SsoNonce, searched.SsoNonce);
        }

        [Fact]
        public void Session_GuidToStringCanBeParsedBack()
        {
            var guid = Guid.NewGuid();

            var asString = guid.ToString();

            var parsed = Guid.Parse(asString);

            Assert.Equal(guid, parsed);
        }
    }
}
