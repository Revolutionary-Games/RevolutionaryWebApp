namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Fixtures;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Server.Authorization;
using Server.Controllers;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class TranslationQualityContributionTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;

    public TranslationQualityContributionTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task RecheckingQualityChargesTheAdminAndReturnsTheNewCountInTheirProfile()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var connection = fixture.GetConnectionStringForAdditionalDatabase();
        builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
            options.UseNpgsql(connection).UseSnakeCaseNamingConvention());
        builder.Services.AddDbContext<NotificationsEnabledDb>(options =>
            options.UseNpgsql(connection).UseSnakeCaseNamingConvention());
        builder.Services.AddControllers().AddApplicationPart(typeof(TranslationProjectsController).Assembly);
        builder.Services.AddSignalR();
        builder.Services.AddScoped<ModerationService>();
        builder.Services.AddScoped<ModerationSessionService>();
        builder.Services.AddSingleton<CustomMemoryCache>();
        builder.Services.AddSingleton(Substitute.For<IDownloadQuota>());
        builder.Services.AddScoped<TokenOrCookieAuthenticationMiddleware>();
        builder.Services.AddScoped<CSRFCheckerMiddleware>();
        builder.Services.AddSingleton(Substitute.For<IModelUpdateNotificationSender>());
        var jobs = Substitute.For<IBackgroundJobClient>();
        builder.Services.AddSingleton(jobs);
        var csrf = Substitute.For<ITokenVerifier>();
        csrf.IsValidCSRFToken("test-token", Arg.Any<User>(), true).Returns(true);
        builder.Services.AddSingleton(csrf);

        await using var app = builder.Build();
        app.UseRouting();
        app.UseMiddleware<TokenOrCookieAuthenticationMiddleware>();
        app.UseMiddleware<CSRFCheckerMiddleware>();
        app.MapControllers();

        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var testId = Guid.NewGuid().ToString("N");
        var admin = new User($"contribution-{testId}@example.com", $"contribution-{testId}")
        {
            Id = Random.Shared.NextInt64(100000, long.MaxValue),
        };
        admin.Groups.Add(await database.UserGroups.SingleAsync(group => group.Id == GroupType.Admin));
        var session = new Session
        {
            UserId = admin.Id, User = admin, CachedUserGroups = admin.ProcessGroupDataFromLoadedGroups(),
        };
        var project = new TranslationProject
        {
            Slug = testId, Name = "Quality check fixture", QualityChecks = TranslationQualityCheck.CSharpFormatting,
        };
        database.Sessions.Add(session);
        database.TranslationProjects.Add(project);
        await database.SaveChangesAsync();

        try
        {
            await app.StartAsync();
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add(HeaderNames.Cookie,
                $"{AppInfo.SessionCookieName}={session.Id}:{admin.Id}");
            client.DefaultRequestHeaders.Add("X-CSRF-Token", "test-token");
            var profileUrl = $"/api/v1/UserManagement/{admin.Id}";
            var before = await client.GetFromJsonAsync<UserDTO>(profileUrl);
            Assert.NotNull(before);
            Assert.Equal(0, before.CurrentContribution);

            using var response = await client.PostAsync(
                "/api/v1/admin/translationProjects/qualityChecks/recheck", null);
            response.EnsureSuccessStatusCode();
            jobs.Received(1).Create(Arg.Is<Job>(job => job.Type == typeof(CheckTranslationQualityJob) &&
                (long)job.Args[0] == project.Id && (bool)job.Args[1]), Arg.Any<IState>());

            var stored = await database.UserContributionCounts.SingleAsync(count => count.UserId == admin.Id);
            Assert.Equal(1000, stored.CurrentContribution);
            var after = await client.GetFromJsonAsync<UserDTO>(profileUrl);
            Assert.NotNull(after);
            Assert.Equal(admin.Id, after.Id);
            Assert.Equal(1000, after.CurrentContribution);
            Assert.Equal(100000, after.ContributionLimit);
        }
        finally
        {
            await database.AdminActions.Where(action => action.PerformedById == admin.Id).ExecuteDeleteAsync();
            await database.Users.Where(user => user.Id == admin.Id).ExecuteDeleteAsync();
            await database.TranslationProjects.Where(item => item.Id == project.Id).ExecuteDeleteAsync();
        }
    }
}
