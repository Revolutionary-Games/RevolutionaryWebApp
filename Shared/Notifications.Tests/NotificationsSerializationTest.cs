namespace ThriveDevCenter.Shared.Notifications.Tests
{
    using System;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Models;
    using Xunit;

    public class NotificationsSerializationTest
    {
        [Fact]
        public void NotificationJsonConverter_AddsTypeOnce()
        {
            var options = new JsonSerializerOptions() { Converters = { new NotificationJsonConverter() } };

            var original = new LFSListUpdated
            {
                Type = ListItemChangeType.ItemAdded,
                Item = new LFSProjectInfo()
                {
                    Name = "something",
                    Slug = "smth",
                    Public = true,
                    CreatedAt = DateTime.Parse("2021-01-02T06:12:00"),
                    UpdatedAt = DateTime.Parse("2021-01-02T06:15:00"),
                }
            };

            var serialized = JsonSerializer.Serialize((SerializedNotification)original, options);

            Assert.Single(Regex.Matches(serialized, nameof(SerializedNotification.NotificationType)));
        }

        [Fact]
        public void LFSUpdateRoundTripSerialization()
        {
            var options = new JsonSerializerOptions() { Converters = { new NotificationJsonConverter() } };

            var original = new LFSListUpdated
            {
                Type = ListItemChangeType.ItemAdded,
                Item = new LFSProjectInfo()
                {
                    Name = "something",
                    Public = true,
                    Slug = "smth",
                    CreatedAt = DateTime.Parse("2021-01-02T06:12:00"),
                    UpdatedAt = DateTime.Parse("2021-01-02T06:15:00"),
                }
            };

            var serialized = JsonSerializer.Serialize((SerializedNotification)original, options);

            var deserialized = JsonSerializer.Deserialize<SerializedNotification>(serialized, options);

            Assert.IsType<LFSListUpdated>(deserialized);

            var casted = (LFSListUpdated)deserialized;

            Assert.Equal(original.Type, casted.Type);

            var item = casted.Item;

            Assert.Equal(original.Item.Name, item.Name);
            Assert.Equal(original.Item.Public, item.Public);
            Assert.Equal(original.Item.Slug, item.Slug);
            Assert.Equal(original.Item.CreatedAt, item.CreatedAt);
            Assert.Equal(original.Item.UpdatedAt, item.UpdatedAt);
        }
    }
}
