namespace ThriveDevCenter.Shared.Notifications.Tests
{
    using System;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Xunit;

    public class NotificationsSerializationTest
    {
        [Fact]
        public void NotificationJsonConverter_AddsTypeOnce()
        {
            var options = new JsonSerializerOptions() { Converters = { new NotificationJsonConverter() } };

            var original = new LFSProjectInfo()
            {
                Name = "something",
                Public = true,
                Size = 123,
                CreatedAt = DateTime.Parse("2021-01-02T06:12:00"),
                LastUpdated = DateTime.Parse("2021-01-02T06:15:00"),
            };

            var serialized = JsonSerializer.Serialize((SerializedNotification)original, options);

            Assert.Single(Regex.Matches(serialized, nameof(SerializedNotification.NotificationType)));
        }

        [Fact]
        public void LFSUpdateRoundTripSerialization()
        {
            var options = new JsonSerializerOptions() { Converters = { new NotificationJsonConverter() } };

            var original = new LFSProjectInfo()
            {
                Name = "something",
                Public = true,
                Size = 123,
                CreatedAt = DateTime.Parse("2021-01-02T06:12:00"),
                LastUpdated = DateTime.Parse("2021-01-02T06:15:00"),
            };

            var serialized = JsonSerializer.Serialize((SerializedNotification)original, options);

            var deserialized = JsonSerializer.Deserialize<SerializedNotification>(serialized, options);

            Assert.IsType<LFSProjectInfo>(deserialized);

            var casted = (LFSProjectInfo)deserialized;

            Assert.Equal(original.Name, casted.Name);
            Assert.Equal(original.Public, casted.Public);
            Assert.Equal(original.Size, casted.Size);
            Assert.Equal(original.CreatedAt, casted.CreatedAt);
            Assert.Equal(original.LastUpdated, casted.LastUpdated);
        }
    }
}
