namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Jobs;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class TranslationResourceDeletionTests
{
    [Fact]
    public async Task DeletingAndRestoringProjectPreservesChildrenAndHidesData()
    {
        await using var database = CreateDatabase();
        SeedProject(database);
        var actor = new User("admin@example.com", "Admin");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.Admin));
        database.Users.Add(actor);
        await database.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;

        var controller =
            new TranslationResourcesController(database, NullLogger<TranslationResourcesController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };

        var listed = (await controller.List(TranslationResourceKind.Projects)).Value!;
        var key = Assert.Single(listed.Results).ResourceKey!;

        Assert.IsType<OkResult>(await controller.Delete(TranslationResourceKind.Projects, key));
        Assert.Empty(await database.TranslationValues.ToArrayAsync());
        Assert.Single(await database.TranslationValues.IgnoreQueryFilters().ToArrayAsync());
        Assert.IsType<NotFoundResult>((await new TranslationsController(database).Project("test")).Result);
        Assert.IsType<OkResult>(await controller.Restore(TranslationResourceKind.Projects, key));
        Assert.Single(await database.TranslationValues.ToArrayAsync());
        Assert.Equal(2, await database.AdminActions.CountAsync());
    }

    [Theory]
    [InlineData(TranslationResourceKind.Projects)]
    [InlineData(TranslationResourceKind.Languages)]
    [InlineData(TranslationResourceKind.Dictionaries)]
    [InlineData(TranslationResourceKind.Terms)]
    [InlineData(TranslationResourceKind.LanguageTerms)]
    public async Task RestoresDeletedResourceInFreshContext(TranslationResourceKind kind)
    {
        await using var database = CreateDatabase();
        SeedProject(database);
        var dictionary = new TranslationDictionary { Id = 1, Name = "Dictionary", Deleted = true };
        database.TranslationDictionaries.Add(dictionary);
        database.DictionaryTerms.Add(new DictionaryTerm { DictionaryId = 1, Word = "Shovel", Deleted = true });
        database.DictionaryLanguageTerms.Add(new DictionaryLanguageTerm
        {
            DictionaryId = 1, LanguageCode = "fi", Word = "Lapio", Deleted = true,
        });
        var project = await database.TranslationProjects.FindAsync(1L);
        project!.Deleted = true;
        var language = await database.TranslationProjectLanguages.FindAsync(1L, "fi");
        language!.Deleted = true;
        var actor = new User("admin@example.com", "Admin");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.Admin));
        database.Users.Add(actor);
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationResourcesController(database,
            NullLogger<TranslationResourcesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        var listed = (await controller.List(kind, deleted: true)).Value!;
        var key = Assert.Single(listed.Results).ResourceKey!;

        // A separate HTTP request has no tracked entity to satisfy FindAsync's identity-map lookup.
        database.ChangeTracker.Clear();
        Assert.IsType<OkResult>(await controller.Restore(kind, key));
        Assert.Empty((await controller.List(kind, deleted: true)).Value!.Results);
        Assert.Single((await controller.List(kind)).Value!.Results);
        Assert.Single(await database.AdminActions.ToArrayAsync());
    }

    [Fact]
    public async Task RestoringMissingResourceReturnsNotFound()
    {
        await using var database = CreateDatabase();
        var controller = new TranslationResourcesController(database,
            NullLogger<TranslationResourcesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var key = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            System.Text.Encoding.UTF8.GetBytes("[999]"));
        Assert.IsType<NotFoundResult>(await controller.Restore(TranslationResourceKind.Projects, key));
    }

    [Fact]
    public async Task DeletedLanguageHidesItsDataAndDeletedDictionaryHidesTerms()
    {
        await using var database = CreateDatabase();
        SeedProject(database);
        var dictionary = new TranslationDictionary { Id = 1, Name = "Dictionary" };
        database.TranslationDictionaries.Add(dictionary);
        database.DictionaryTerms.Add(new DictionaryTerm { DictionaryId = 1, Word = "term" });
        await database.SaveChangesAsync();

        var language = await database.TranslationProjectLanguages.SingleAsync();
        language.Deleted = true;
        dictionary.Deleted = true;
        await database.SaveChangesAsync();

        var editor = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database);
        Assert.IsType<NotFoundResult>((await editor.List(1, "fi")).Result);
        var dictionaries = new TranslationDictionariesController(
            NullLogger<TranslationDictionariesController>.Instance, database);
        Assert.IsType<NotFoundResult>((await dictionaries.Terms(1)).Result);
        Assert.Empty(await database.DictionaryTerms.ToArrayAsync());
        Assert.Single(await database.DictionaryTerms.IgnoreQueryFilters().ToArrayAsync());
    }

    [Fact]
    public async Task DeletedProjectCannotImportOrPublishEvenWhenTracked()
    {
        await using var database = CreateDatabase();
        SeedProject(database);
        await database.SaveChangesAsync();
        var project = await database.TranslationProjects.SingleAsync();
        project.Deleted = true;
        await database.SaveChangesAsync();

        var locks = Substitute.For<ILocalTempFileLocks>();
        var tokens = Substitute.For<ITranslationGithubTokenProvider>();
        var import = new ImportTranslationProjectJob(NullLogger<ImportTranslationProjectJob>.Instance,
            database, locks, Substitute.For<IBackgroundJobClient>());
        var publish = new PublishTranslationProjectJob(database, locks, tokens,
            new ConfigurationBuilder().Build(), NullLogger<PublishTranslationProjectJob>.Instance);

        await import.Execute(1, CancellationToken.None);
        await publish.Execute(1, CancellationToken.None);
        locks.DidNotReceiveWithAnyArgs().GetTempFilePath(null!);
        await tokens.DidNotReceiveWithAnyArgs().GetToken(CancellationToken.None);
        Assert.Empty(await database.TranslationSyncRuns.IgnoreQueryFilters().ToArrayAsync());
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }

    private static void SeedProject(ApplicationDbContext database)
    {
        database.TranslationProjects.Add(new TranslationProject
        {
            Id = 1, Slug = "test", Name = "Test", Format = TranslationFormat.Resx,
            RepositoryFullName = "example/translations",
        });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "key" });
        database.TranslationValues.Add(new TranslationValue { ProjectId = 1, Key = "key", LanguageCode = "fi" });
    }
}
