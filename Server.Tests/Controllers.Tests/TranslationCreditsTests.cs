namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Authorization;
using Server.Controllers;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class TranslationCreditsTests
{
    [Fact]
    public async Task ExportCountsHistoricalContributionsPerUserAndCreditsSuggestionAuthors()
    {
        await using var database = CreateDatabase();
        var first = new User("first@example.com", "First") { DisplayName = "First Translator" };
        var second = new User("second@example.com", "Second") { DisplayName = " " };
        var reviewer = new User("reviewer@example.com", "Reviewer");
        var project = new TranslationProject { Slug = "test", Name = "Test", Enabled = false };
        var otherProject = new TranslationProject { Slug = "other", Name = "Other" };
        await database.Users.AddRangeAsync(first, second, reviewer);
        await database.TranslationProjects.AddRangeAsync(project, otherProject);
        await database.SaveChangesAsync();

        var language = new TranslationProjectLanguage
        {
            Project = project, ProjectId = project.Id, LanguageCode = "fi", Deleted = true,
        };
        var unit = new TranslationUnit { Project = project, ProjectId = project.Id, Key = "key", Active = false };
        var value = new TranslationValue
        {
            ProjectId = project.Id, Key = unit.Key, LanguageCode = language.LanguageCode,
            Unit = unit, Language = language, Text = "Current translation", LastEditedByUserId = second.Id,
        };
        await database.TranslationProjectLanguages.AddAsync(language);
        await database.TranslationUnits.AddAsync(unit);
        await database.TranslationValues.AddAsync(value);
        await database.TranslationChanges.AddRangeAsync(new TranslationChange
            {
                ProjectId = project.Id, Key = unit.Key, LanguageCode = language.LanguageCode, Value = value,
                ActorUserId = first.Id, ContributorUserId = first.Id, CountsAsContribution = true,
                NewText = "Overwritten translation",
            },
            new TranslationChange
            {
                ProjectId = project.Id, Key = unit.Key, LanguageCode = language.LanguageCode, Value = value,
                ActorUserId = reviewer.Id, ContributorUserId = first.Id, CountsAsContribution = true,
                Kind = TranslationChangeKind.SuggestionAccepted, NewText = "Overwritten suggestion",
            },
            new TranslationChange
            {
                ProjectId = project.Id, Key = unit.Key, LanguageCode = language.LanguageCode, Value = value,
                ActorUserId = second.Id, ContributorUserId = second.Id, CountsAsContribution = true,
                NewText = value.Text,
            },
            new TranslationChange
            {
                ProjectId = project.Id, Key = unit.Key, LanguageCode = language.LanguageCode, Value = value,
                ActorUserId = reviewer.Id, ContributorUserId = reviewer.Id, CountsAsContribution = false,
            },
            new TranslationChange { ProjectId = project.Id, CountsAsContribution = true },
            new TranslationChange
            {
                ProjectId = otherProject.Id, ContributorUserId = reviewer.Id, CountsAsContribution = true,
            });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var result = Assert.IsType<FileContentResult>(await CreateController(database)
            .ExportCredits(project.Id, CancellationToken.None));

        Assert.Equal("translators.json", result.FileDownloadName);
        Assert.Equal("application/json", result.ContentType);
        using var json = JsonDocument.Parse(result.FileContents);
        var entries = json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(new[] { "name", "contributionCount", "email" },
            entries[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal("First Translator", entries[0].GetProperty("name").GetString());
        Assert.Equal(2, entries[0].GetProperty("contributionCount").GetInt32());
        Assert.Equal(first.Email, entries[0].GetProperty("email").GetString());
        Assert.Equal("Second", entries[1].GetProperty("name").GetString());
        Assert.Equal(1, entries[1].GetProperty("contributionCount").GetInt32());
        Assert.Equal(second.Email, entries[1].GetProperty("email").GetString());
    }

    [Fact]
    public async Task EmptyProjectExportsAnEmptyArrayAndMissingOrDeletedProjectsReturnNotFound()
    {
        await using var database = CreateDatabase();
        var project = new TranslationProject { Slug = "empty", Name = "Empty" };
        await database.TranslationProjects.AddAsync(project);
        await database.SaveChangesAsync();
        var controller = CreateController(database);

        var file = Assert.IsType<FileContentResult>(await controller.ExportCredits(project.Id, CancellationToken.None));
        using var json = JsonDocument.Parse(file.FileContents);
        Assert.Equal(0, json.RootElement.GetArrayLength());
        Assert.IsType<NotFoundResult>(await controller.ExportCredits(project.Id + 1, CancellationToken.None));

        project.Deleted = true;
        await database.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.ExportCredits(project.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(GroupType.User)]
    [InlineData(GroupType.Developer)]
    [InlineData(GroupType.Admin)]
    public async Task CreditsControllerRequiresAdminAccess(GroupType? access)
    {
        var http = new DefaultHttpContext();
        if (access != null)
        {
            var user = new User("test@example.com", "Test");
            user.Groups.Add(new UserGroup(access.Value, access.Value.ToString()));
            user.ProcessGroupDataFromLoadedGroups();
            http.User = new ClaimsPrincipal(new ClaimsIdentity("Test"));
            http.Items[AppInfo.CurrentUserMiddlewareKey] = user;
            http.Items[AppInfo.AuthenticationScopeRestrictionMiddleWareKey] = AuthenticationScopeRestriction.None;
        }

        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
        var authorization = typeof(TranslationProjectsController)
            .GetCustomAttribute<AuthorizeBasicAccessLevelFilterAttribute>()!;
        await authorization.OnAuthorizationAsync(context);

        if (access == null)
        {
            Assert.IsType<UnauthorizedResult>(context.Result);
        }
        else if (access == GroupType.Admin)
        {
            Assert.Null(context.Result);
        }
        else
        {
            Assert.IsType<ForbidResult>(context.Result);
        }
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }

    private static TranslationProjectsController CreateController(NotificationsEnabledDb database)
    {
        return new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance, database,
            Substitute.For<IBackgroundJobClient>());
    }
}
