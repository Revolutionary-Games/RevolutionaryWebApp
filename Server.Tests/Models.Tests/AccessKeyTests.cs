namespace ThriveDevCenter.Server.Tests.Models.Tests
{
    using System;
    using System.Net;
    using System.Text.Json;
    using Server.Models;
    using Shared.Models;
    using Xunit;

    public class AccessKeyTests
    {
        [Fact]
        public void AccessKey_JsonSerializeWorks()
        {
            var key = new AccessKey()
            {
                Id = 123,
                Description = "some description",
                HashedKeyCode = "a hash",
                KeyCode = Guid.NewGuid().ToString(),
                KeyType = AccessKeyType.DevBuilds,
                LastUsed = DateTime.UtcNow,
                LastUsedFrom = new IPAddress(new byte[] { 127, 0, 0, 1 })
            };

            var serialized = JsonSerializer.Serialize(key);

            var deserialized = JsonSerializer.Deserialize<AccessKey>(serialized);

            Assert.NotNull(deserialized);

            Assert.Equal(key.Id, deserialized.Id);
            Assert.Equal(key.Description, deserialized.Description);
            Assert.Equal(key.HashedKeyCode, deserialized.HashedKeyCode);
            Assert.Equal(key.KeyCode, deserialized.KeyCode);
            Assert.Equal(key.KeyType, deserialized.KeyType);
            Assert.Equal(key.LastUsed, deserialized.LastUsed);
            Assert.Equal(key.LastUsedFrom, deserialized.LastUsedFrom);
            Assert.Equal(key.CreatedAt, deserialized.CreatedAt);
        }
    }
}
