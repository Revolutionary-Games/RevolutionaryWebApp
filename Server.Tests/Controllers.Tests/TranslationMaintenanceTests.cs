namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class TranslationMaintenanceTests
{
    [Fact]
    public async Task ReopeningNotifiesOnceAndClearsOnlyReopeningSubscriptions()
    {
        await using var database = CreateDatabase();
        var project = new TranslationProject { Id = 1, Slug = "test", Name = "Test", MaintenanceClosed = true };
        database.TranslationProjects.Add(project);
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "en" });
        var actor = new User("user@example.com", "User") { Id = 1 };
        database.Users.Add(actor);
        await database.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var subscriptions = new TranslationSubscriptionsController(database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        Assert.IsType<NoContentResult>(await subscriptions.SubscribeToReopening(1));
        Assert.IsType<NoContentResult>(await subscriptions.SubscribeToReopening(1));
        Assert.Single(database.TranslationSubscriptions);
        database.TranslationSubscriptions.Add(new TranslationSubscription
        {
            UserId = 1, ProjectId = 1, LanguageCode = "en", EventType = TranslationSubscriptionEventType.Comment,
        });
        await database.SaveChangesAsync();

        var controller = new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance,
            database, Substitute.For<IBackgroundJobClient>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        Assert.IsType<NoContentResult>(await controller.SetMaintenance(1, false));
        Assert.False(project.GetDTO(false).ClosedForMaintenance);
        Assert.Equal(UserNotificationType.TranslationProjectReopened, Assert.Single(database.UserNotifications).Type);
        Assert.Equal(TranslationSubscriptionEventType.Comment,
            Assert.Single(database.TranslationSubscriptions).EventType);
        Assert.IsType<OkObjectResult>(await controller.SetMaintenance(1, false));
        Assert.Single(database.UserNotifications);
        Assert.IsType<ConflictObjectResult>(await subscriptions.SubscribeToReopening(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastConflictReopensOnlyAutomaticallyClosedProjects(bool manuallyClosed)
    {
        await using var database = CreateDatabase();
        var admin = new User("admin@example.com", "Admin") { Id = 1 };
        admin.Groups.Add(new UserGroup(GroupType.Admin, "Admin"));
        database.Users.Add(admin);
        var project = new TranslationProject
        {
            Id = 1, Name = "Test", Slug = "test", MaintenanceClosed = manuallyClosed,
        };
        database.TranslationProjects.Add(project);
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "key" });
        database.TranslationValues.Add(new TranslationValue { ProjectId = 1, Key = "key", LanguageCode = "fi" });
        var conflicts = Enumerable.Range(0, 2).Select(_ => new TranslationImportConflict
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", IncomingText = "Remote",
        }).ToArray();
        database.TranslationImportConflicts.AddRange(conflicts);
        database.TranslationSubscriptions.Add(new TranslationSubscription
        {
            UserId = 1, ProjectId = 1, LanguageCode = "fi",
            EventType = TranslationSubscriptionEventType.ProjectReopened,
        });
        await database.SaveChangesAsync();

        await TranslationMaintenance.ConflictDetected(database, project);

        await database.SaveChangesAsync();

        Assert.True(project.ClosedForMaintenance);
        var notice = Assert.Single(database.UserNotifications);
        Assert.Equal(UserNotificationType.TranslationImportConflict, notice.Type);
        Assert.Equal(NotificationPriority.High, notice.Priority);
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = admin;
        var controller = new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance,
            database, Substitute.For<IBackgroundJobClient>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        Assert.IsType<ConflictObjectResult>(await controller.SetMaintenance(1, false));
        Assert.IsType<NoContentResult>(await controller.ResolveConflict(1, conflicts[0].Id, true));
        Assert.True(project.ClosedForMaintenance);
        Assert.IsType<NoContentResult>(await controller.ResolveConflict(1, conflicts[1].Id, true));
        Assert.False(project.ConflictClosed);
        Assert.Equal(manuallyClosed, project.ClosedForMaintenance);
        Assert.Equal(manuallyClosed ? 1 : 2, await database.UserNotifications.CountAsync());
        Assert.Equal(manuallyClosed ? 1 : 0, await database.TranslationSubscriptions.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaintenanceRejectsTranslationSaves(bool conflictClosure)
    {
        await using var database = CreateDatabase();
        database.TranslationProjects.Add(new TranslationProject
        {
            Id = 1, MaintenanceClosed = !conflictClosure, ConflictClosed = conflictClosure,
        });
        await database.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = new User("user@example.com", "User");
        var controller = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        var result = await controller.Save(new TranslationSaveRequest
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Text = "Blocked",
        });
        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Empty(database.TranslationChanges);
        Assert.Empty(database.TranslationValues);
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }
}
