namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Models.Pages;
using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class ModerationRevisionJournalTests
{
    [Theory]
    [InlineData("Original page body", true, false)]
    [InlineData("Original page body", true, true)]
    [InlineData("", true, false)]
    [InlineData("", false, false)]
    public async Task PageRollbackUsesNativeHistoryWhenAvailable(string originalContent, bool hasPreviousVersion,
        bool laterEdit)
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor = new User("spam@example.org", "Spammer");
        var good = new User("good@example.org", "Contributor");
        actor.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        good.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        database.Users.AddRange(actor, good);
        var page = new VersionedPage("Original title") { LatestContent = originalContent, LastEditorId = good.Id };
        database.VersionedPages.Add(page);
        await database.SaveChangesAsync();

        var spamContent = hasPreviousVersion ? "Spam page body" : new string('x', 1024 * 1024);
        page.Title = "Spam title";
        page.LatestContent = spamContent;
        page.LastEditorId = actor.Id;
        if (hasPreviousVersion)
            database.PageVersions.Add(page.CreatePreviousVersion(1, originalContent));
        await database.SaveChangesAsync();

        var revision = await database.ModerationRevisions.SingleAsync();
        using var before = JsonDocument.Parse(revision.BeforeJson);
        using var after = JsonDocument.Parse(revision.AfterJson);
        if (hasPreviousVersion)
        {
            Assert.Equal(1, before.RootElement.GetProperty(nameof(VersionedPage.LatestContent)).GetInt32());
            Assert.NotEqual(spamContent, after.RootElement.GetProperty(nameof(VersionedPage.LatestContent)).GetString());
        }
        else
        {
            Assert.False(before.RootElement.TryGetProperty(nameof(VersionedPage.LatestContent), out _));
            Assert.False(after.RootElement.TryGetProperty(nameof(VersionedPage.LatestContent), out _));
        }

        database.SuppressModerationJournal = true;
        if (laterEdit)
        {
            // Keep the journaled metadata the same so only the content check can prevent the rollback.
            page.LatestContent = "Later valid content";
            database.PageVersions.Add(page.CreatePreviousVersion(2, spamContent));
            await database.SaveChangesAsync();
        }

        var skipped = await ModerationRevisionJournal.Revert(database, actor.Id, revision.CreatedAt);
        await database.SaveChangesAsync();
        Assert.Equal(laterEdit ? 1 : 0, skipped);
        Assert.Equal(laterEdit ? "Later valid content" : hasPreviousVersion ? originalContent : spamContent,
            page.LatestContent);
        Assert.Equal(laterEdit ? "Spam title" : "Original title", page.Title);
        Assert.Equal(laterEdit ? actor.Id : good.Id, page.LastEditorId);
    }

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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DictionaryRollbackRespectsLaterEditorsAndRetention(bool laterEdit, bool expiredRevision)
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
        var revision = await database.ModerationRevisions.SingleAsync();
        if (expiredRevision)
        {
            revision.CreatedAt = DateTime.UtcNow.AddMonths(-13);
            await database.SaveChangesAsync();
        }

        if (laterEdit)
        {
            term.Text = "Corrected translation";
            term.LastEditedByUserId = good.Id;
            await database.SaveChangesAsync();
        }

        database.SuppressModerationJournal = true;
        var skipped = await ModerationRevisionJournal.Revert(database, actor.Id, DateTime.MinValue);
        await database.SaveChangesAsync();
        Assert.Equal(laterEdit ? 1 : 0, skipped);
        Assert.Equal(laterEdit ? "Corrected translation" : expiredRevision ? "Spam" : "Solu", term.Text);
        Assert.Equal(expiredRevision ? actor.Id : good.Id, term.LastEditedByUserId);
        Assert.Equal(laterEdit ? 2 : 1, await database.ModerationRevisions.CountAsync());
    }
}
