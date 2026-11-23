namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class ModerationRevisionJournalTests
{
    [Fact]
    public async Task SecondPassRemovesLateEditsThatRestoreAlreadyRevertedSpam()
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor = new User("spam@example.org", "Spammer");
        actor.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        var good = new User("good@example.org", "Contributor");
        good.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        database.Users.AddRange(actor, good);
        var term = new DictionaryTermTranslation
        {
            DictionaryId = 1, Word = "Cell", LanguageCode = "fi", Text = "Good", LastEditedByUserId = good.Id,
        };
        database.DictionaryTermTranslations.Add(term);
        await database.SaveChangesAsync();
        term.Text = "Spam";
        term.LastEditedByUserId = actor.Id;
        await database.SaveChangesAsync();
        var cutoff = DateTime.UtcNow.AddHours(-24);
        database.SuppressModerationJournal = true;
        await ModerationRevisionJournal.Revert(database, actor.Id, cutoff);
        await database.SaveChangesAsync();
        Assert.Equal("Good", term.Text);

        // This request loaded the spam before the first purge and saves its edit after it.
        database.SuppressModerationJournal = false;
        database.Entry(term).Property(item => item.Text).OriginalValue = "Spam";
        database.Entry(term).Property(item => item.LastEditedByUserId).OriginalValue = actor.Id;
        term.Text = "Late spam";
        term.LastEditedByUserId = actor.Id;
        await database.SaveChangesAsync();
        database.SuppressModerationJournal = true;
        await ModerationRevisionJournal.Revert(database, actor.Id, cutoff);
        await database.SaveChangesAsync();
        Assert.Equal("Good", term.Text);
        Assert.Equal(good.Id, term.LastEditedByUserId);
        await ModerationRevisionJournal.Revert(database, actor.Id, cutoff);
        Assert.Equal("Good", term.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DictionaryRollbackRestoresPreviousTextAndPreservesLaterEditors(bool laterEdit)
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor = new User("spam@example.org", "Spammer");
        actor.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        var good = new User("good@example.org", "Contributor");
        good.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(actor);
        await database.Users.AddAsync(good);
        var term = new DictionaryTermTranslation
        {
            DictionaryId = 1, Word = "Cell", LanguageCode = "fi", Text = "Solu", LastEditedByUserId = good.Id,
        };
        await database.DictionaryTermTranslations.AddAsync(term);
        await database.SaveChangesAsync();
        term.Text = "Spam";
        term.LastEditedByUserId = actor.Id;
        await database.SaveChangesAsync();
        Assert.Single(await database.ModerationRevisions.ToListAsync());
        if (laterEdit)
        {
            term.Text = "Corrected translation";
            term.LastEditedByUserId = good.Id;
            await database.SaveChangesAsync();
        }

        database.SuppressModerationJournal = true;
        var skipped = await ModerationRevisionJournal.Revert(database, actor.Id, DateTime.UtcNow.AddHours(-24));
        await database.SaveChangesAsync();
        Assert.Equal(laterEdit ? 1 : 0, skipped);
        Assert.Equal(laterEdit ? "Corrected translation" : "Solu", term.Text);
        Assert.Equal(good.Id, term.LastEditedByUserId);
        Assert.Equal(laterEdit ? 2 : 1, await database.ModerationRevisions.CountAsync());
    }
}
