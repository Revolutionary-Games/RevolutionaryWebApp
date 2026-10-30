namespace RevolutionaryWebApp.Server.Tests.Models.Tests;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Xunit;

public class TranslationModelConfigurationTests
{
    [Fact]
    public async Task DeletingSuggestionAuthorPreservesAcceptedWorkAndClearsAttribution()
    {
        await using var database = CreateDatabase();
        var author = new User("translator@example.com", "Translator");
        database.Users.Add(author);
        var unit = new TranslationUnit { ProjectId = 1, Key = "key" };
        var value = new TranslationValue
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Text = "Accepted work",
            LastEditedByUser = author, Unit = unit,
        };
        database.TranslationValues.Add(value);
        database.TranslationSuggestions.Add(new TranslationSuggestion
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", AuthorUser = author, Value = value,
            Text = "Accepted work", Status = TranslationSuggestionStatus.Accepted,
        });
        database.TranslationChanges.Add(new TranslationChange
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Value = value,
            ActorUser = author, ContributorUser = author, NewText = "Accepted work",
        });
        await database.SaveChangesAsync();
        database.Users.Remove(author);
        await database.SaveChangesAsync();

        Assert.Empty(await database.TranslationSuggestions.ToListAsync());
        Assert.Equal("Accepted work", Assert.Single(await database.TranslationValues.ToListAsync()).Text);
        Assert.Null(value.LastEditedByUserId);
        var change = Assert.Single(await database.TranslationChanges.ToListAsync());
        Assert.Null(change.ActorUserId);
        Assert.Null(change.ContributorUserId);
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }
}
