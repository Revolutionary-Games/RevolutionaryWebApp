namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

public class UserReportsControllerTests
{
    [Fact]
    public async Task ReportSnapshotsAuthorAndHidesCommentUntilLastReportIsDismissed()
    {
        await using var database = CreateDatabase();
        var reporter = AddUser(database, 1);
        var target = AddUser(database, 2);
        var moderator = AddUser(database, 3, GroupType.ContentModerator);
        var secondReporter = AddUser(database, 4);
        var comment = AddComment(database, target);
        await database.SaveChangesAsync();
        var controller = CreateController(database, reporter);
        var request = new CreateUserReportRequest
        {
            Target = UserReportTarget.TranslationComment, TargetId = comment.Id, Reason = UserReportReason.Spam,
        };
        Assert.IsType<CreatedResult>((await controller.Create(request)).Result);
        Assert.IsType<ConflictObjectResult>((await controller.Create(request)).Result);
        var report = await database.UserReports.SingleAsync();
        Assert.Equal(target.UserName, report.TargetUserName);
        Assert.Equal(comment.RawMarkdown, report.ContentSnapshot);
        Assert.True(comment.Reported);
        var discussion = new TranslationDiscussionController(NullLogger<TranslationDiscussionController>.Instance,
            database) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.Empty((await discussion.Comments(1, "key", "fi")).Value!);
        var activity = new TranslationsController(database)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        Assert.Empty((await activity.Recent("test", "fi", TranslationActivityView.Comments)).Value!.Results);
        activity.HttpContext.Items[AppInfo.CurrentUserMiddlewareKey] = reporter;
        Assert.True(Assert.Single((await activity.Recent("test", "fi", TranslationActivityView.Comments))
            .Value!.Results).Reported);
        discussion.HttpContext.Items[AppInfo.CurrentUserMiddlewareKey] = reporter;
        Assert.True(Assert.Single((await discussion.Comments(1, "key", "fi")).Value!).Reported);
        Assert.IsType<CreatedResult>((await CreateController(database, secondReporter).Create(request)).Result);
        var moderationController = CreateController(database, moderator);
        var dismiss = new ResolveUserReportRequest
        {
            Resolution = UserReportResolution.Dismissed, Note = "The comment is appropriate",
        };
        Assert.IsType<OkObjectResult>(await moderationController.Resolve(report.Id, dismiss));
        Assert.True(comment.Reported);
        var other = await database.UserReports.SingleAsync(item => item.Id != report.Id);
        Assert.IsType<OkObjectResult>(await moderationController.Resolve(other.Id, dismiss));
        Assert.False(comment.Reported);
        Assert.Equal(2, await database.AdminActions.CountAsync());
        Assert.Equal(2, await database.UserReportNotes.CountAsync());
        Assert.IsType<ConflictObjectResult>(await moderationController.Resolve(report.Id, dismiss));
    }

    [Theory]
    [InlineData(ModerationAction.Mute, false)]
    [InlineData(ModerationAction.Ban, true)]
    public async Task BanRevokesSessionsWhileMuteKeepsThem(ModerationAction action, bool revoke)
    {
        await using var database = CreateDatabase();
        var actor = AddUser(database, 1, GroupType.ContentModerator);
        var target = AddUser(database, 2);
        var report = new UserReport { TargetUserId = target.Id, TargetUserName = target.UserName };
        await database.UserReports.AddAsync(report);
        await database.Sessions.AddAsync(new Session { UserId = target.Id, LastUsedFrom = IPAddress.Loopback });
        await database.SaveChangesAsync();
        var response = await CreateController(database, actor).Resolve(report.Id, new ResolveUserReportRequest
        {
            Resolution = UserReportResolution.Approved, Action = action, Days = 3, Note = "Spam",
        });
        Assert.IsType<OkObjectResult>(response);
        Assert.Single(await database.Sessions.ToListAsync());
        Assert.False(target.Suspended);
        Assert.Equal(revoke, target.ModerationPending);
        Assert.False(target.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.User));
        if (revoke)
        {
            var pending = await database.PendingUserModeration.SingleAsync();
            await CreateModerationService(database).ProcessPendingAction(pending.Id, false, CancellationToken.None);
            Assert.False(target.ModerationPending);
        }

        Assert.Equal(revoke ? 0 : 1, await database.Sessions.CountAsync());
        Assert.Equal(revoke, target.Suspended);
        Assert.Equal(!revoke, target.MutedUntil > DateTime.UtcNow);
        Assert.Equal(revoke ? 2 : 1, await database.AdminActions.CountAsync());
    }

    [Theory]
    [InlineData(GroupType.ContentModerator, 71, false, true)]
    [InlineData(GroupType.ContentModerator, 73, false, false)]
    [InlineData(GroupType.ContentModerator, 1, true, false)]
    [InlineData(GroupType.Admin, 1000, false, true)]
    public async Task PurgePermissionsUseFirstContributionRatherThanAccountAge(GroupType group, int hours,
        bool allTime, bool allowed)
    {
        await using var database = CreateDatabase();
        var actor = AddUser(database, 1, group);
        var target = AddUser(database, 2);
        target.CreatedAt = DateTime.UtcNow.AddYears(-5);
        await database.UserContributionCounts.AddAsync(new UserContributionCount
        {
            UserId = target.Id, FirstContributedAt = DateTime.UtcNow.AddHours(-hours),
        });
        var report = new UserReport { TargetUserId = target.Id, TargetUserName = target.UserName };
        await database.UserReports.AddAsync(report);
        await database.SaveChangesAsync();
        var response = await CreateController(database, actor).Resolve(report.Id, new ResolveUserReportRequest
        {
            Resolution = UserReportResolution.Approved, Action = ModerationAction.PurgeAndBan,
            AllTime = allTime, Note = "Serious spammer",
        });
        if (allowed)
        {
            Assert.IsType<OkObjectResult>(response);
            Assert.True(target.ModerationPending);
            Assert.False(target.Suspended);
            await CreateModerationService(database).ProcessPendingAction(
                (await database.PendingUserModeration.SingleAsync()).Id, false, CancellationToken.None);
            Assert.True(target.Suspended);
            Assert.Equal(DateTime.MaxValue, target.SuspendedUntil);
            Assert.Single(await database.RegistrationBlocks.ToListAsync());
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(response);
            Assert.False(target.Suspended);
            Assert.Null(report.ResolvedAt);
            Assert.Empty(await database.AdminActions.ToListAsync());
        }
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(48, true)]
    public async Task ExtendedPurgeRequiresSudoBeforeChangingTheReport(int hours, bool allTime)
    {
        await using var database = CreateDatabase();
        var actor = AddUser(database, 1, GroupType.Admin);
        var target = AddUser(database, 2);
        var report = new UserReport { TargetUserId = target.Id, TargetUserName = target.UserName };
        await database.UserReports.AddAsync(report);
        await database.SaveChangesAsync();

        var response = await CreateController(database, actor).Resolve(report.Id, new ResolveUserReportRequest
        {
            Resolution = UserReportResolution.Approved, Action = ModerationAction.PurgeAndBan,
            PurgeHours = hours, AllTime = allTime, Note = "Serious spammer",
        });

        var forbidden = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(AppInfo.SudoModeWebsiteOnlyMessage, forbidden.Value);
        Assert.Null(report.ResolvedAt);
        Assert.False(target.ModerationPending);
        Assert.Empty(await database.PendingUserModeration.ToListAsync());
    }

    [Fact]
    public async Task PurgeRevertsSpamAndKeepsLaterContributionsAndOutOfWindowComments()
    {
        await using var database = CreateDatabase();
        var actor = AddUser(database, 1, GroupType.Admin);
        var target = AddUser(database, 2);
        var comment = AddComment(database, target);
        var oldComment = new TranslationComment
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", AuthorUserId = target.Id,
            CreatedAt = DateTime.UtcNow.AddHours(-49), RawMarkdown = "Old useful comment",
        };
        await database.TranslationComments.AddAsync(oldComment);
        var value = database.TranslationValues.Local.Single();
        var project = database.TranslationProjects.Local.Single();
        project.QualityChecks = TranslationQualityCheck.CSharpFormatting;
        value.Unit.SourceText = "Source {0}";
        value.Text = "Spam";
        value.State = TranslationState.Translated;
        value.LastEditedByUserId = target.Id;
        TranslationQualityChecks.UpdateProblems(database, project, value);
        await database.TranslationChanges.AddAsync(new TranslationChange
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", ActorUserId = target.Id,
            OldText = "Good {0}", NewText = "Spam", OldState = TranslationState.Verified,
            NewState = TranslationState.Translated,
        });
        var laterValue = new TranslationValue
        {
            ProjectId = 1, Key = "key", LanguageCode = "en", Text = "Later useful edit",
            State = TranslationState.Verified, LastEditedByUserId = actor.Id,
        };
        await database.TranslationProjectLanguages.AddAsync(new TranslationProjectLanguage
        {
            ProjectId = 1, LanguageCode = "en",
        });
        await database.TranslationValues.AddAsync(laterValue);
        await database.TranslationChanges.AddAsync(new TranslationChange
        {
            ProjectId = 1, Key = "key", LanguageCode = "en", ActorUserId = target.Id,
            OldText = "Good source", NewText = "Bad source", OldState = TranslationState.Verified,
            NewState = TranslationState.Translated,
        });
        await database.Sessions.AddAsync(new Session
        {
            UserId = target.Id, LastUsedFrom = IPAddress.Parse("::ffff:192.0.2.1"),
        });
        target.LastKnownIPAddress = "192.0.2.3";
        var report = new UserReport { TargetUserId = target.Id, TargetUserName = target.UserName };
        await database.UserReports.AddAsync(report);
        await database.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await CreateController(database, actor).Resolve(report.Id,
            new ResolveUserReportRequest
            {
                Resolution = UserReportResolution.Approved, Action = ModerationAction.PurgeAndBan,
                PurgeHours = 48, Note = "Serious spammer",
            }));
        Assert.False(comment.Deleted);
        var pending = await database.PendingUserModeration.SingleAsync();
        var moderation = CreateModerationService(database);
        await moderation.ProcessPendingAction(pending.Id, false, CancellationToken.None);
        Assert.True(comment.Deleted);
        Assert.False(oldComment.Deleted);
        Assert.Equal("Good {0}", value.Text);
        Assert.Equal(TranslationState.Verified, value.State);
        Assert.Empty(await database.TranslationProblems.ToListAsync());
        Assert.Equal("Later useful edit", laterValue.Text);
        Assert.True(await RegistrationBlocks.IsBlocked(database, target.Email, null));
        Assert.True(await RegistrationBlocks.IsBlocked(database, "another@example.org", IPAddress.Parse("192.0.2.1")));
        Assert.False(await RegistrationBlocks.IsBlocked(database, "another@example.org", IPAddress.Parse("192.0.2.2")));
        var blocks = await database.RegistrationBlocks.ToListAsync();
        Assert.All(blocks, block => Assert.Equal(pending.Id, block.ModerationActionId));
        Assert.Equal(target.Email, Assert.Single(blocks, block => block.IPAddress == null).Email);
        var ipBlocks = blocks.Where(block => block.IPAddress != null).ToList();
        Assert.Equal(new[] { "192.0.2.1", "192.0.2.3" }, ipBlocks.Select(block => block.IPAddress).Order());
        Assert.All(ipBlocks, block =>
        {
            Assert.Null(block.Email);
            Assert.Null(block.NormalizedEmail);
        });
        Assert.Empty(await database.Sessions.ToListAsync());
        Assert.Single(await database.TranslationChanges.Where(change =>
            change.Kind == TranslationChangeKind.ModerationRevert).ToListAsync());

        // Simulate a write that started before the account was restricted and completed after the first purge.
        var lateComment = new TranslationComment
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", AuthorUserId = target.Id, RawMarkdown = "Late spam",
        };
        await database.TranslationComments.AddAsync(lateComment);
        await database.Sessions.AddAsync(new Session { UserId = target.Id, LastUsedFrom = IPAddress.Loopback });
        value.Text = "Late spam edit";
        value.State = TranslationState.Translated;
        value.LastEditedByUserId = target.Id;
        await database.TranslationChanges.AddAsync(new TranslationChange
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", ActorUserId = target.Id,
            OldText = "Spam", NewText = "Late spam edit", OldState = TranslationState.Translated,
            NewState = TranslationState.Translated, CreatedAt = pending.CreatedAt,
        });
        await database.SaveChangesAsync();
        var originalCutoff = pending.PurgeCutoff;
        await moderation.ProcessPendingAction(pending.Id, true, CancellationToken.None);
        Assert.True(lateComment.Deleted);
        Assert.Equal("Good {0}", value.Text);
        Assert.Equal(TranslationState.Verified, value.State);
        Assert.False(oldComment.Deleted);
        Assert.Equal(originalCutoff, pending.PurgeCutoff);
        Assert.NotNull(pending.CleanedUpAt);
        Assert.Empty(await database.Sessions.ToListAsync());
        blocks = await database.RegistrationBlocks.ToListAsync();
        Assert.Equal(4, blocks.Count);
        Assert.All(blocks, block => Assert.Equal(pending.Id, block.ModerationActionId));
        Assert.Single(blocks, block => block.IPAddress == IPAddress.Loopback.ToString());
        var logs = await database.AdminActions.CountAsync();
        await moderation.ProcessPendingAction(pending.Id, true, CancellationToken.None);
        Assert.Equal(logs, await database.AdminActions.CountAsync());
    }

    [Fact]
    public async Task RejectedReportsCannotPunishUsersAndStaffCannotBePurgedByModerators()
    {
        await using var database = CreateDatabase();
        var actor = AddUser(database, 1, GroupType.ContentModerator);
        var target = AddUser(database, 2, GroupType.Admin);
        var report = new UserReport { TargetUserId = target.Id };
        await database.UserReports.AddAsync(report);
        await database.SaveChangesAsync();
        var controller = CreateController(database, actor);
        Assert.IsType<BadRequestObjectResult>(await controller.Resolve(report.Id, new ResolveUserReportRequest
        {
            Resolution = UserReportResolution.Dismissed, Action = ModerationAction.Ban, Note = "Invalid decision",
        }));
        Assert.IsType<BadRequestObjectResult>(await controller.Resolve(report.Id, new ResolveUserReportRequest
        {
            Resolution = UserReportResolution.Approved, Action = ModerationAction.Ban, Note = "Invalid target",
        }));
        Assert.False(target.Suspended);
        Assert.Null(report.ResolvedAt);
    }

    [Fact]
    public async Task RecentActivityIsPrivateAndCachedForRepeatedRequests()
    {
        await using var database = CreateDatabase();
        var owner = AddUser(database, 1);
        var other = AddUser(database, 2);
        AddComment(database, owner);
        await database.SaveChangesAsync();
        var controller = new UserActivityController(database, new CustomMemoryCache())
        {
            ControllerContext = new ControllerContext { HttpContext = Context(other) },
        };
        Assert.IsType<ForbidResult>((await controller.Get(owner.Id)).Result);
        controller.ControllerContext = new ControllerContext { HttpContext = Context(owner) };
        var first = (await controller.Get(owner.Id)).Value;
        Assert.NotEmpty(first!);
        Assert.Same(first, (await controller.Get(owner.Id)).Value);
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }

    private static User AddUser(NotificationsEnabledDb database, long id, GroupType group = GroupType.User)
    {
        var user = new User($"user{id}@example.com", $"User{id}") { Id = id };
        if (group != GroupType.User)
        {
            var membership = database.UserGroups.Find(group) ?? new UserGroup(group, group.ToString());
            user.Groups.Add(membership);
        }

        user.ProcessGroupDataFromLoadedGroups();
        database.Users.Add(user);
        return user;
    }

    private static TranslationComment AddComment(NotificationsEnabledDb database, User author)
    {
        database.TranslationProjects.Add(new TranslationProject
        {
            Id = 1, Name = "Test", Slug = "test", Enabled = true, SourceLanguage = "en",
        });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "key" });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
        {
            ProjectId = 1, LanguageCode = "fi", Enabled = true,
        });
        database.TranslationValues.Add(new TranslationValue { ProjectId = 1, Key = "key", LanguageCode = "fi" });
        var comment = new TranslationComment
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", AuthorUserId = author.Id, RawMarkdown = "Comment",
        };
        database.TranslationComments.Add(comment);
        return comment;
    }

    private static UserReportsController CreateController(NotificationsEnabledDb database, User actor)
    {
        var configuration = new ConfigurationBuilder().Build();
        var sessions = new ModerationSessionService(database,
            Substitute.For<IHubContext<NotificationsHub, INotifications>>());
        return new UserReportsController(database, new ModerationService(database, configuration, sessions,
                Substitute.For<IBackgroundJobClient>()), sessions,
            NullLogger<UserReportsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = Context(actor) },
        };
    }

    private static ModerationService CreateModerationService(NotificationsEnabledDb database)
    {
        return new ModerationService(database, new ConfigurationBuilder().Build(),
            new ModerationSessionService(database, Substitute.For<IHubContext<NotificationsHub, INotifications>>()),
            Substitute.For<IBackgroundJobClient>());
    }

    private static DefaultHttpContext Context(User actor)
    {
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        return context;
    }
}
