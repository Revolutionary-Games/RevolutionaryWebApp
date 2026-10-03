namespace RevolutionaryWebApp.Shared.Tests.Notifications.Tests;

using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shared.Models;
using Shared.Notifications;
using Xunit;

public class NotificationsSerializationTests
{
    [Fact]
    public void NotificationJsonConverter_AddsTypeOnce()
    {
        var options = new JsonSerializerOptions { Converters = { new NotificationJsonConverter() } };

        var original = new LFSListUpdated
        {
            Type = ListItemChangeType.ItemAdded,
            Item = new LFSProjectInfo
            {
                Name = "something",
                Slug = "smth",
                Public = true,
                CreatedAt = DateTime.Parse("2021-01-02T06:12:00"),
                UpdatedAt = DateTime.Parse("2021-01-02T06:15:00"),
            },
        };

        var serialized = JsonSerializer.Serialize((SerializedNotification)original, options);

        Assert.Single(Regex.Matches(serialized, nameof(SerializedNotification.NotificationType)));
    }

    [Fact]
    public void LFSUpdateRoundTripSerialization()
    {
        var options = new JsonSerializerOptions { Converters = { new NotificationJsonConverter() } };

        var original = new LFSListUpdated
        {
            Type = ListItemChangeType.ItemAdded,
            Item = new LFSProjectInfo
            {
                Name = "something",
                Public = true,
                Slug = "smth",
                CreatedAt = DateTime.Parse("2021-01-02T06:12:00"),
                UpdatedAt = DateTime.Parse("2021-01-02T06:15:00"),
            },
        };

        var serialized = JsonSerializer.Serialize((SerializedNotification)original, options);

        var deserialized = JsonSerializer.Deserialize<SerializedNotification>(serialized, options);
        Assert.NotNull(deserialized);

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

    [Fact]
    public void TranslationDtoRoundTripThroughNotificationConverter()
    {
        var options = new JsonSerializerOptions { Converters = { new NotificationJsonConverter() } };
        var original = new TranslationEditorUpdated
        {
            Item = new TranslationEditorDTO
            {
                ProjectId = 42,
                ProjectSlug = "thrive",
                Key = "welcome.message",
                LanguageCode = "fr",
                SourceText = "Welcome",
                SourceTextTranslatedFrom = "Welcome",
                Text = "Bienvenue",
                State = TranslationState.Translated,
            },
        };

        var serialized = JsonSerializer.Serialize((SerializedNotification)original, options);
        var deserialized = Assert.IsType<TranslationEditorUpdated>(
            JsonSerializer.Deserialize<SerializedNotification>(serialized, options));

        Assert.Equal(original.Item.ProjectId, deserialized.Item.ProjectId);
        Assert.Equal(original.Item.Key, deserialized.Item.Key);
        Assert.Equal(original.Item.Text, deserialized.Item.Text);
        Assert.Equal(original.Item.State, deserialized.Item.State);
    }

    [Fact]
    public void TranslationStateSerializesByNameAndRejectsUnknownValues()
    {
        Assert.Equal("\"Translated\"", JsonSerializer.Serialize(TranslationState.Translated));
        Assert.Equal(TranslationState.Verified, JsonSerializer.Deserialize<TranslationState>("\"Verified\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TranslationState>("\"Unknown\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TranslationState>("1"));
    }

    [Fact]
    public void UserNotificationTypeSerializesByNameAndRejectsUnknownValues()
    {
        var notification = new UserNotificationDTO { Type = UserNotificationType.TranslationEdited };
        var serialized = JsonSerializer.Serialize(notification);

        Assert.Contains("\"Type\":\"TranslationEdited\"", serialized);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UserNotificationType>("\"Unknown\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UserNotificationType>("1"));
    }

    [Fact]
    public void TranslationChangeKindSerializesByName()
    {
        Assert.Equal("\"SourceChanged\"", JsonSerializer.Serialize(TranslationChangeKind.SourceChanged));
        Assert.Equal(TranslationChangeKind.UserEdit,
            JsonSerializer.Deserialize<TranslationChangeKind>("\"UserEdit\""));
    }

    [Fact]
    public void TranslationFormatAndSuggestionStatusSerializeByName()
    {
        Assert.Equal("\"Resx\"", JsonSerializer.Serialize(TranslationFormat.Resx));
        Assert.Equal(TranslationFormat.Gettext, JsonSerializer.Deserialize<TranslationFormat>("\"Gettext\""));
        Assert.Equal("\"Accepted\"", JsonSerializer.Serialize(TranslationSuggestionStatus.Accepted));
        Assert.Equal(TranslationSuggestionStatus.Open,
            JsonSerializer.Deserialize<TranslationSuggestionStatus>("\"Open\""));
    }
}

public sealed class TranslationEditorUpdated : ModelUpdated<TranslationEditorDTO>
{
}
