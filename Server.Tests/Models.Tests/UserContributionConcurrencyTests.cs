namespace RevolutionaryWebApp.Server.Tests.Models.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Filters;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class UserContributionConcurrencyTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;

    public UserContributionConcurrencyTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentFirstContributionsArePreservedAndOnlyLogLimitOnce()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await CreateUser(database);
        const int requests = 8;
        int admitted = 0;
        var allAdmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // ReSharper disable once AccessToDisposedClosure
            await Task.WhenAll(Enumerable.Range(0, requests).Select(_ => RunFilter(provider, user, 10000, async () =>
            {
                if (Interlocked.Increment(ref admitted) == requests)
                    allAdmitted.SetResult();

                // All actions must be able to run before any one completes.
                await allAdmitted.Task.WaitAsync(TimeSpan.FromSeconds(15));
                return new OkResult();
            })));

            var count = await database.UserContributionCounts.AsNoTracking().SingleAsync(c => c.UserId == user.Id);
            Assert.Equal(requests * 10000, count.CurrentContribution);
            Assert.True(count.ContributionExceeded);
            Assert.Equal(1, await database.LogEntries.CountAsync(log => log.TargetUserId == user.Id));

            bool actionRan = false;
            var rejected = await RunFilter(provider, user, 1, () =>
            {
                actionRan = true;
                return Task.FromResult<IActionResult>(new OkResult());
            });
            Assert.False(actionRan);
            Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(rejected).StatusCode);
        }
        finally
        {
            await database.LogEntries.Where(log => log.TargetUserId == user.Id).ExecuteDeleteAsync();
            await database.Users.Where(current => current.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task FailedActionIsNotCountedAndDailyResetUsesStoredBudget()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await CreateUser(database);
        try
        {
            await RunFilter(provider, user, 10000, () => Task.FromResult<IActionResult>(new BadRequestResult()));
            var count = await database.UserContributionCounts.AsNoTracking().SingleAsync(c => c.UserId == user.Id);
            Assert.Equal(0, count.CurrentContribution);

            await RunFilter(provider, user, 10000, () => Task.FromResult<IActionResult>(new OkResult()));
            await new ResetDailyContributionsJob(database).Execute(CancellationToken.None);
            count = await database.UserContributionCounts.AsNoTracking().SingleAsync(c => c.UserId == user.Id);
            Assert.Equal(0, count.CurrentContribution);
            Assert.Equal(15000, count.ContributionLimit);
            Assert.False(count.ContributionExceeded);
        }
        finally
        {
            await database.LogEntries.Where(log => log.TargetUserId == user.Id).ExecuteDeleteAsync();
            await database.Users.Where(current => current.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    private static async Task<User> CreateUser(ApplicationDbContext database)
    {
        // The fixture seeds explicit IDs without advancing the user sequence.
        var user = new User($"contribution-{Guid.NewGuid():N}@example.com", "Contribution test")
        {
            Id = Random.Shared.NextInt64(100000, long.MaxValue),
        };
        user.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        database.Users.Add(user);
        await database.SaveChangesAsync();
        return user;
    }

    private static async Task<IActionResult?> RunFilter(IServiceProvider provider, User user, int amount,
        Func<Task<IActionResult>> action)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity()),
        };
        http.Items[AppInfo.CurrentUserMiddlewareKey] = user;
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        var executing = new ActionExecutingContext(actionContext, filters, new Dictionary<string, object?>(),
            new object());
        await new CountContributionAttribute(amount).OnActionExecutionAsync(executing, async () =>
            new ActionExecutedContext(actionContext, filters, executing.Controller) { Result = await action() });
        return executing.Result;
    }

    private ServiceProvider CreateProvider()
    {
        return new ServiceCollection().AddDbContext<ApplicationDbContext>(options => options
                .UseNpgsql(fixture.GetConnectionStringForAdditionalDatabase()).UseSnakeCaseNamingConvention())
            .BuildServiceProvider();
    }
}
