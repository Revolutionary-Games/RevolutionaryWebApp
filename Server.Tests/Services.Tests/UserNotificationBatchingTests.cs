namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Xunit;

public class UserNotificationBatchingTests
{
    [Theory]
    [InlineData(UserNotificationType.TranslationUpdated, TranslationActivityView.Edits)]
    [InlineData(UserNotificationType.TranslationKeyAdded, TranslationActivityView.Keys)]
    [InlineData(UserNotificationType.TranslationSourceChanged, TranslationActivityView.Source)]
    [InlineData(UserNotificationType.TranslationComments, TranslationActivityView.Comments)]
    [InlineData(UserNotificationType.TranslationSuggestionCreated, TranslationActivityView.Suggestions)]
    public async Task FiftyUnreadEventsCombineWithoutLosingDeduplicationRecords(UserNotificationType type,
        TranslationActivityView view)
    {
        await using var database = CreateDatabase();
        database.TranslationProjects.Add(new TranslationProject { Id = 1, Slug = "test", SourceLanguage = "en" });
        for (var index = 0; index < 50; ++index)
            database.UserNotifications.Add(Notice(type, index));
        await database.SaveChangesAsync();
        await Combine(database);
        await Combine(database);

        var visible = await database.UserNotifications.Where(notice => notice.CombinedIntoId == null).ToListAsync();
        var summary = Assert.Single(visible);
        Assert.Equal(50, summary.CombinedCount);
        Assert.Equal(NotificationPriority.Low, summary.Priority);
        var language = type == UserNotificationType.TranslationSourceChanged ? "en" : "fi";
        Assert.Equal($"/translations/test/{language}/recent?types={view}", summary.TargetPath);
        Assert.Contains("50", summary.Message);
        Assert.Equal(50, await database.UserNotifications.CountAsync());
        Assert.Equal(50, await database.UserNotifications.Select(notice => notice.DeduplicationKey).Distinct()
            .CountAsync());

        database.UserNotifications.Add(Notice(type, 51));
        await database.SaveChangesAsync();
        await Combine(database);
        Assert.Equal(51, summary.CombinedCount);
        Assert.Single(await database.UserNotifications.Where(notice => notice.CombinedIntoId == null).ToListAsync());
    }

    [Fact]
    public async Task CommentsAcrossLanguagesOpenProjectCommentHistory()
    {
        await using var database = CreateDatabase();
        var first = Notice(UserNotificationType.TranslationComments, 1);
        var second = Notice(UserNotificationType.TranslationComments, 2);
        second.TargetPath = "/translations/test/sv/edit?key=other";
        database.UserNotifications.AddRange(first, second);
        await database.SaveChangesAsync();
        await Combine(database);
        var parent = await database.UserNotifications.SingleAsync(notice => notice.CombinedIntoId == null);
        Assert.Equal("/translations/test/recent?types=Comments", parent.TargetPath);
        database.UserNotifications.Add(Notice(UserNotificationType.TranslationComments, 3));
        await database.SaveChangesAsync();
        await Combine(database);
        Assert.Equal(3, parent.CombinedCount);
        Assert.Equal("/translations/test/recent?types=Comments", parent.TargetPath);
    }

    [Fact]
    public async Task DifferentRecipientsScopesAndPersonalNoticesRemainSeparate()
    {
        await using var database = CreateDatabase();
        var first = Notice(UserNotificationType.TranslationUpdated, 1);
        var otherLanguage = Notice(UserNotificationType.TranslationUpdated, 2);
        otherLanguage.TargetPath = "/translations/test/sv/edit?key=key";
        var otherProject = Notice(UserNotificationType.TranslationUpdated, 3);
        otherProject.TargetPath = "/translations/other/fi/edit?key=key";
        var otherUser = Notice(UserNotificationType.TranslationUpdated, 4);
        otherUser.UserId = 2;
        database.UserNotifications.AddRange(first, otherLanguage, otherProject, otherUser);
        var personalTypes = new[]
        {
            UserNotificationType.TranslationEdited, UserNotificationType.DictionaryTermEdited,
        };
        foreach (var type in personalTypes)
        {
            database.UserNotifications.Add(Notice(type, 10 + (int)type));
            database.UserNotifications.Add(Notice(type, 20 + (int)type));
        }

        await database.SaveChangesAsync();
        await Combine(database);
        Assert.Equal(8, await database.UserNotifications.CountAsync(notice => notice.CombinedIntoId == null));
        var personalNotices = await database.UserNotifications
            .Where(notice => notice.Type == UserNotificationType.TranslationEdited ||
                notice.Type == UserNotificationType.DictionaryTermEdited).ToListAsync();
        Assert.All(personalNotices, notice => Assert.Equal(NotificationPriority.Normal, notice.Priority));
    }

    [Fact]
    public async Task ReadEmailedAndOldNoticesAreNotCombinedWithFreshEvents()
    {
        await using var database = CreateDatabase();
        var read = Notice(UserNotificationType.TranslationUpdated, 1);
        read.ReadAt = DateTime.UtcNow;
        var emailed = Notice(UserNotificationType.TranslationUpdated, 2);
        emailed.EmailSentAt = DateTime.UtcNow;
        var old = Notice(UserNotificationType.TranslationUpdated, 3);
        old.CreatedAt = DateTime.UtcNow.AddHours(-5);
        database.UserNotifications.AddRange(read, emailed, old, Notice(UserNotificationType.TranslationUpdated, 4));
        await database.SaveChangesAsync();
        await Combine(database);
        Assert.All(await database.UserNotifications.ToListAsync(), notice => Assert.Null(notice.CombinedIntoId));
    }

    [Fact]
    public async Task CandidateQueryTranslatesWithThePostgresProvider()
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .LogTo(_ => throw new QueryTranslatedException(), new[] { CoreEventId.QueryExecutionPlanned }).Options);

        // Stop after EF translates the actual production query, before opening a database connection.
        await Assert.ThrowsAsync<QueryTranslatedException>(() => Combine(database));
    }

    private static ApplicationDbContext CreateDatabase()
    {
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    private static UserNotification Notice(UserNotificationType type, int index)
    {
        return new UserNotification
        {
            UserId = 1, Type = type, DeduplicationKey = $"{type}:{index}", Message = "One event",
            TargetPath = $"/translations/test/fi/edit?key=key{index}",
        };
    }

    private static Task Combine(ApplicationDbContext database)
    {
        return UserNotificationBatching.Combine(database, new ConfigurationBuilder().Build(), CancellationToken.None);
    }

    private sealed class QueryTranslatedException : Exception;
}
