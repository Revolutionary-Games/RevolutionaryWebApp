namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Filters;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class TranslationProblemsControllerTests
{
    [Fact]
    public async Task SaveReturnsWarningsIgnoreRemovesThemFromCountsAndFixDeletesThem()
    {
        await using var database = CreateDatabase();
        var context = await Seed(database);
        var editor = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = context,
        };
        var value = await database.TranslationValues.Include(translationValue => translationValue.Unit)
            .ThenInclude(translationUnit => translationUnit.Project).SingleAsync();
        var saved = (await editor.Save(new TranslationSaveRequest
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Text = "hei", Version = value.Version,
        })).Value!;
        Assert.Equal(3, saved.Problems.Length);
        Assert.All(saved.Problems, problem => Assert.False(problem.Ignored));
        Assert.Equal(TranslationState.Translated, value.State);
        Assert.NotNull(value.PendingRevision);
        var pendingRevision = value.PendingRevision;
        var revision = value.Unit.Project.CurrentRevision;
        var version = value.Version;

        var languages = new TranslationsController(database);
        Assert.Equal(3, Assert.Single((await languages.Languages("test", CancellationToken.None)).Value!)
            .CheckFailures);
        Assert.Single((await editor.List(1, "fi", problemsOnly: true)).Value!);

        var problemsController = new TranslationProblemsController(database) { ControllerContext = context };
        var beforeIgnore = DateTime.UtcNow;
        foreach (var problem in saved.Problems)
            Assert.IsType<NoContentResult>(await problemsController.Ignore(problem.Id));

        var afterIgnore = DateTime.UtcNow;
        Assert.Equal(0, Assert.Single((await languages.Languages("test", CancellationToken.None)).Value!)
            .CheckFailures);
        Assert.Empty((await editor.List(1, "fi", problemsOnly: true)).Value!);
        var actor = (User)context.HttpContext.Items[AppInfo.CurrentUserMiddlewareKey]!;
        Assert.All((await problemsController.Get(1, "key", "fi")).Value!, problem =>
        {
            Assert.True(problem.Ignored);
            Assert.Equal(actor.Id, problem.IgnoredByUserId);
            Assert.InRange(problem.IgnoredAt!.Value, beforeIgnore, afterIgnore);
        });
        var loaded = (await editor.Get(1, "key", "fi", CancellationToken.None)).Value!;
        Assert.All(loaded.Problems, problem =>
        {
            Assert.Equal(actor.Id, problem.IgnoredByUserId);
            Assert.InRange(problem.IgnoredAt!.Value, beforeIgnore, afterIgnore);
        });
        Assert.Equal(pendingRevision, value.PendingRevision);
        Assert.Equal(revision, value.Unit.Project.CurrentRevision);
        Assert.Equal(version, value.Version);
        Assert.Single(database.TranslationChanges);

        saved = (await editor.Save(new TranslationSaveRequest
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Text = "Hei {0}!", Version = version,
        })).Value!;
        Assert.Empty(saved.Problems);
        Assert.Empty(database.TranslationProblems);
    }

    [Fact]
    public async Task RepeatedIgnorePreservesOriginalUserAndTime()
    {
        await using var database = CreateDatabase();
        var context = await Seed(database);
        var value = await database.TranslationValues.Include(item => item.Unit).ThenInclude(unit => unit.Project)
            .Include(item => item.Problems).SingleAsync();
        value.Text = "hei";
        TranslationQualityChecks.UpdateProblems(database, value.Unit.Project, value);
        await database.SaveChangesAsync();
        var problem = value.Problems.First();
        var controller = new TranslationProblemsController(database) { ControllerContext = context };
        Assert.IsType<NoContentResult>(await controller.Ignore(problem.Id));
        var ignoredBy = problem.IgnoredByUserId;
        var ignoredAt = problem.IgnoredAt;
        var otherActor = new User("other@example.com", "Other translator");
        otherActor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(otherActor);
        await database.SaveChangesAsync();
        context.HttpContext.Items[AppInfo.CurrentUserMiddlewareKey] = otherActor;

        Assert.IsType<NoContentResult>(await controller.Ignore(problem.Id));
        var persisted = await database.TranslationProblems.AsNoTracking().SingleAsync(item => item.Id == problem.Id);
        Assert.Equal(ignoredBy, persisted.IgnoredByUserId);
        Assert.Equal(ignoredAt, persisted.IgnoredAt);
    }

    [Fact]
    public async Task DeletingIgnoringUserPreservesWarningAndIgnoreTime()
    {
        await using var database = CreateDatabase();
        var context = await Seed(database);
        var value = await database.TranslationValues.Include(item => item.Unit).ThenInclude(unit => unit.Project)
            .Include(item => item.Problems).SingleAsync();
        value.Text = "hei";
        TranslationQualityChecks.UpdateProblems(database, value.Unit.Project, value);
        await database.SaveChangesAsync();
        var problem = value.Problems.First();
        var controller = new TranslationProblemsController(database) { ControllerContext = context };
        Assert.IsType<NoContentResult>(await controller.Ignore(problem.Id));
        var ignoredAt = problem.IgnoredAt;
        database.Users.Remove((User)context.HttpContext.Items[AppInfo.CurrentUserMiddlewareKey]!);
        await database.SaveChangesAsync();

        var persisted = await database.TranslationProblems.AsNoTracking().SingleAsync(item => item.Id == problem.Id);
        Assert.True(persisted.Ignored);
        Assert.Null(persisted.IgnoredByUserId);
        Assert.Equal(ignoredAt, persisted.IgnoredAt);
    }

    [Fact]
    public async Task SavingUnchangedTextAlsoRunsChecks()
    {
        await using var database = CreateDatabase();
        var context = await Seed(database);
        var value = await database.TranslationValues.SingleAsync();
        value.Text = "hei";
        await database.SaveChangesAsync();
        var editor = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = context,
        };
        var saved = (await editor.Save(new TranslationSaveRequest
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Text = value.Text, Version = value.Version,
        })).Value!;

        Assert.Equal(3, saved.Problems.Length);
        Assert.Empty(database.TranslationChanges);
    }

    [Fact]
    public async Task IgnoreEnforcesTranslationPermissionsAndMaintenance()
    {
        await using var database = CreateDatabase();
        var context = await Seed(database);
        var value = await database.TranslationValues.Include(item => item.Unit).ThenInclude(unit => unit.Project)
            .Include(item => item.Problems).SingleAsync();
        value.Text = "hei";
        TranslationQualityChecks.UpdateProblems(database, value.Unit.Project, value);
        await database.SaveChangesAsync();
        var problem = value.Problems.First();
        var actor = (User)context.HttpContext.Items[AppInfo.CurrentUserMiddlewareKey]!;
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.NotLoggedIn));
        var controller = new TranslationProblemsController(database) { ControllerContext = context };
        var forbidden = await Assert.ThrowsAsync<HttpResponseException>(() => controller.Ignore(problem.Id));
        Assert.Equal(403, forbidden.Status);
        Assert.False(problem.Ignored);

        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        value.Unit.Project.MaintenanceClosed = true;
        await database.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await controller.Ignore(problem.Id));
        Assert.False(problem.Ignored);

        value.Unit.Project.MaintenanceClosed = false;
        value.Unit.Active = false;
        await database.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.Ignore(problem.Id));
    }

    [Fact]
    public async Task WarningNavigationUsesFileOrderAndExcludesIgnoredInactiveAndDeletedValues()
    {
        await using var database = CreateDatabase();
        var context = await Seed(database);
        var value = await database.TranslationValues.Include(item => item.Unit).ThenInclude(unit => unit.Project)
            .Include(item => item.Problems).Include(item => item.Language).SingleAsync();
        value.Unit.Position = 50;
        value.Text = "hei";
        TranslationQualityChecks.UpdateProblems(database, value.Unit.Project, value);
        foreach (var key in new[] { "first", "ignored", "inactive", "deleted" })
        {
            var language = key == "deleted" ?
                new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "deleted", Deleted = true } :
                value.Language;
            database.TranslationValues.Add(new TranslationValue
            {
                ProjectId = 1, Key = key, LanguageCode = language.LanguageCode, Language = language,
                Unit = new TranslationUnit { ProjectId = 1, Key = key, Position = 10, Active = key != "inactive" },
                Problems =
                [
                    new TranslationProblem
                    {
                        ProjectId = 1, Key = key, LanguageCode = language.LanguageCode,
                        Kind = TranslationProblemKind.EndingPunctuation, Ignored = key == "ignored",
                    },
                ],
            });
        }

        await database.SaveChangesAsync();
        var editor = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = context,
        };
        Assert.Equal(new[] { "first", "key" }, (await editor.List(1, "fi", problemsOnly: true)).Value!
            .Select(item => item.Key));
        Assert.Equal(4, Assert.Single((await new TranslationsController(database)
            .Languages("test", CancellationToken.None)).Value!).CheckFailures);
    }

    [Fact]
    public async Task SourceEditImmediatelyRechecksTargetText()
    {
        await using var database = CreateDatabase();
        var context = await Seed(database);
        var target = await database.TranslationValues.Include(item => item.Unit).ThenInclude(unit => unit.Project)
            .SingleAsync();
        var project = target.Unit.Project;
        target.Text = "Hei!";
        TranslationQualityChecks.UpdateProblems(database, project, target);
        var source = new TranslationValue
        {
            ProjectId = 1, Key = "key", LanguageCode = "en", Text = target.Unit.SourceText, Unit = target.Unit,
            Language = new TranslationProjectLanguage { Project = project, LanguageCode = "en" },
        };
        database.TranslationValues.Add(source);
        var actor = (User)context.HttpContext.Items[AppInfo.CurrentUserMiddlewareKey]!;
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User, GroupType.SourceTextTranslator));
        await database.SaveChangesAsync();
        Assert.Single(await database.TranslationProblems.ToListAsync());
        var editor = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = context,
        };
        var result = (await editor.Save(new TranslationSaveRequest
        {
            ProjectId = 1, Key = "key", LanguageCode = "en", Text = "Hello {1}.", Version = source.Version,
        })).Value!;

        Assert.Empty(result.Problems);
        Assert.Equal(TranslationState.NeedsTranslation, target.State);
        Assert.Empty(await database.TranslationProblems.ToListAsync());

        var saved = (await editor.Save(new TranslationSaveRequest
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Text = "Hei {0}!", Version = target.Version,
        })).Value!;

        Assert.Single(saved.Problems);
        var warning = Assert.Single(await database.TranslationProblems.ToListAsync());
        Assert.Equal("fi", warning.LanguageCode);
        Assert.Contains("{1}", warning.Message);
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }

    private static async Task<ControllerContext> Seed(ApplicationDbContext database)
    {
        var actor = new User("translator@example.com", "Translator");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        database.Users.Add(actor);
        var project = new TranslationProject
        {
            Id = 1, Slug = "test", SourceLanguage = "en", Format = TranslationFormat.Gettext,
            QualityChecks = TranslationQualityCheck.CSharpFormatting | TranslationQualityCheck.SentenceConsistency,
        };
        database.TranslationValues.Add(new TranslationValue
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Text = "Hei {0}!", State = TranslationState.Translated,
            Unit = new TranslationUnit { Project = project, Key = "key", SourceText = "Hello {0}!" },
            Language = new TranslationProjectLanguage { Project = project, LanguageCode = "fi" },
        });
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        return new ControllerContext { HttpContext = context };
    }
}
