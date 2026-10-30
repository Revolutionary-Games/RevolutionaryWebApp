namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Xunit;

public class TranslationSourceChangeHelperTests
{
    [Fact]
    public async Task OneHundredSourceChangesCreateOneNoticeAndInvalidateAllTargets()
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var project = new TranslationProject { Id = 1, Name = "Test", Slug = "test" };
        database.TranslationProjects.Add(project);
        database.TranslationSubscriptions.Add(new TranslationSubscription
        {
            UserId = 1, ProjectId = 1, LanguageCode = "fi",
            EventType = TranslationSubscriptionEventType.SourceChange,
        });

        // One user can subscribe through multiple language pages and should still get just one source notice.
        database.TranslationSubscriptions.Add(new TranslationSubscription
        {
            UserId = 1, ProjectId = 1, LanguageCode = "sv",
            EventType = TranslationSubscriptionEventType.SourceChange,
        });
        for (var index = 0; index < 100; ++index)
        {
            database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = $"key{index}" });
            database.TranslationValues.Add(new TranslationValue
            {
                ProjectId = 1, Key = $"key{index}", LanguageCode = "fi", Text = "Translation",
                State = TranslationState.Translated,
            });
        }

        await database.SaveChangesAsync();
        foreach (var unit in await database.TranslationUnits.ToListAsync())
            await TranslationSourceChangeHelper.MarkTargetsOutdated(database, project, unit, CancellationToken.None);
        await database.SaveChangesAsync();

        Assert.Single(await database.UserNotifications.ToListAsync());
        Assert.All(await database.TranslationValues.ToListAsync(), value =>
            Assert.Equal(TranslationState.NeedsTranslation, value.State));
        Assert.Equal(100, await database.TranslationChanges.CountAsync());
    }
}
