namespace RevolutionaryWebApp.Server.Tests.Authorization.Tests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Server.Authorization;
using Server.Controllers;
using Server.Controllers.Admin;
using Server.Filters;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public sealed class SudoModeAuthorizationTests : IDisposable
{
    private readonly TwoFactorTestContext test = new();
    private readonly DefaultHttpContext http = new();
    private readonly ServiceProvider services;
    private readonly SudoModeService sudo;

    public SudoModeAuthorizationTests()
    {
        services = new ServiceCollection().AddDistributedMemoryCache().AddSingleton(test.Service)
            .BuildServiceProvider();
        sudo = new SudoModeService(test.Database, test.Service, services.GetRequiredService<IDistributedCache>());
        var filterServices = new ServiceCollection().AddSingleton(test.Service).AddSingleton(sudo)
            .BuildServiceProvider();
        http.RequestServices = filterServices;
        http.User = new GenericPrincipal(test.User, []);
        http.Items[AppInfo.CurrentUserMiddlewareKey] = test.User;
        http.Items[AppInfo.CurrentUserSessionMiddleWareKey] = test.Session;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSudoReturnsDistinctChallengeWithSetupStatus(bool enabled)
    {
        if (enabled)
            await test.Enable();

        var context = Context();
        await new RequireSudoModeAttribute().OnAuthorizationAsync(context);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(enabled, Assert.IsType<SudoModeRequiredDTO>(result.Value).TwoFactorEnabled);
        Assert.Equal(AppInfo.SudoModeRequiredHeaderValue, http.Response.Headers[AppInfo.SudoModeResponseHeader]);
        Assert.Equal("no-store", http.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task AllowsActiveSudoAndChallengesAgainAtExpiry()
    {
        await test.Enable();
        Assert.True(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[0]));
        var context = Context();
        await new RequireSudoModeAttribute().OnAuthorizationAsync(context);
        Assert.Null(context.Result);

        test.Clock.Advance(TimeSpan.FromMinutes(15));
        await new RequireSudoModeAttribute().OnAuthorizationAsync(context);
        Assert.IsType<SudoModeRequiredDTO>(Assert.IsType<ObjectResult>(context.Result).Value);
    }

    [Fact]
    public async Task AnonymousRequestsAndEarlierAccessDenialsDoNotPrompt()
    {
        var attribute = new RequireSudoModeAttribute();
        Assert.True(attribute.Order > 0);
        http.Items.Remove(AppInfo.CurrentUserMiddlewareKey);
        var context = Context();
        await attribute.OnAuthorizationAsync(context);
        Assert.IsType<UnauthorizedResult>(context.Result);
        Assert.False(http.Response.Headers.ContainsKey(AppInfo.SudoModeResponseHeader));

        context = Context();
        context.Result = new ForbidResult();
        await attribute.OnAuthorizationAsync(context);
        Assert.IsType<ForbidResult>(context.Result);
        Assert.False(http.Response.Headers.ContainsKey(AppInfo.SudoModeResponseHeader));
    }

    [Fact]
    public async Task ApiCredentialsCannotEnterOrUseSudoEvenWhenAnotherSessionHasIt()
    {
        await test.Enable();
        Assert.True(await sudo.Enter(test.Session, test.User.Id, test.BackupCodes[0]));
        http.Items.Remove(AppInfo.CurrentUserSessionMiddleWareKey);
        var context = Context();
        await new RequireSudoModeAttribute().OnAuthorizationAsync(context);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(AppInfo.SudoModeWebsiteOnlyMessage, result.Value);
        Assert.False(http.Response.Headers.ContainsKey(AppInfo.SudoModeResponseHeader));

        using var controller = new SudoModeController(sudo);
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        result = Assert.IsType<ObjectResult>(await controller.Enter(new TwoFactorCodeForm
        {
            Code = test.BackupCodes[1],
        }));
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(9, TwoFactorService.GetBackupCodeHashes(test.Credential).Count);
    }

    [Fact]
    public async Task HttpPipelineChecksPermissionsAndCsrfBeforeAllowingSudoProtectedAction()
    {
        await test.Enable();
        test.Session.CachedUserGroups = new CachedUserGroups(GroupType.Admin);
        await test.Database.SaveChangesAsync();
        var csrf = Substitute.For<ITokenVerifier>();
        csrf.IsValidCSRFToken("csrf", test.User).Returns(true);
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(registrations =>
            {
                registrations.AddSingleton<ApplicationDbContext>(test.Database);
                registrations.AddSingleton(test.Database);
                registrations.AddSingleton(test.Jobs);
                registrations.AddSingleton(test.Service);
                registrations.AddSingleton(sudo);
                registrations.AddSingleton(csrf);
                registrations.AddSingleton<CustomMemoryCache>();
                registrations.AddScoped<TokenOrCookieAuthenticationMiddleware>();
                registrations.AddScoped<CSRFCheckerMiddleware>();
                registrations.AddRateLimiter(options => options.AddFixedWindowLimiter(RateLimitCategories.LoginLimit,
                    limiter =>
                    {
                        limiter.PermitLimit = 100;
                        limiter.Window = TimeSpan.FromMinutes(1);
                    }));
                registrations.AddAuthentication(options =>
                {
                    options.DefaultForbidScheme = "forbidScheme";
                    options.AddScheme<MyForbidHandler>("forbidScheme", "Handle Forbidden");
                });
                registrations.AddControllers().AddApplicationPart(typeof(SudoModeController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseMiddleware<TokenOrCookieAuthenticationMiddleware>();
                app.UseMiddleware<CSRFCheckerMiddleware>();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).StartAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{AppInfo.SessionCookieName}={test.Session.Id}:{test.User.Id}");
        const string target = "/api/v1/UserManagement/999/groups/0";
        using var missingCsrf = await client.PutAsync(target, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.False(missingCsrf.Headers.Contains(AppInfo.SudoModeResponseHeader));

        client.DefaultRequestHeaders.Add("X-CSRF-Token", "csrf");
        using var challenge = await client.PutAsync(target, null);
        Assert.Equal(HttpStatusCode.Forbidden, challenge.StatusCode);
        Assert.True((await challenge.Content.ReadFromJsonAsync<SudoModeRequiredDTO>())!.TwoFactorEnabled);

        using var entered = await client.PostAsJsonAsync("/api/v1/SudoMode", new TwoFactorCodeForm
        {
            Code = test.BackupCodes[0],
        });
        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        using var retried = await client.PutAsync(target, null);
        Assert.Equal(HttpStatusCode.BadRequest, retried.StatusCode);
        Assert.Contains("Cannot add system-assigned groups", await retried.Content.ReadAsStringAsync());

        // Having sudo mode must not grant administrator permissions.
        test.Session.CachedUserGroups = new CachedUserGroups(GroupType.User);
        using var denied = await client.PutAsync(target, null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False(denied.Headers.Contains(AppInfo.SudoModeResponseHeader));
    }

    [Theory]
    [InlineData(typeof(UserGroupController), nameof(UserGroupController.AddGroup))]
    [InlineData(typeof(UserGroupController), nameof(UserGroupController.RemoveGroup))]
    [InlineData(typeof(BackupController), nameof(BackupController.Delete))]
    [InlineData(typeof(BackupController), nameof(BackupController.GetDownloadLink))]
    [InlineData(typeof(ControlledServersController), nameof(ControlledServersController.ForceTerminateServer))]
    [InlineData(typeof(MaintenanceController), nameof(MaintenanceController.StartMaintenanceOperation))]
    [InlineData(typeof(UserManagementController), nameof(UserManagementController.SuspendUser))]
    public void DangerousEndpointsRequireSudo(Type controller, string method)
    {
        Assert.NotNull(Attribute.GetCustomAttribute(controller.GetMethod(method)!, typeof(RequireSudoModeAttribute)));
    }

    public void Dispose()
    {
        ((ServiceProvider)http.RequestServices).Dispose();
        services.Dispose();
        test.Dispose();
    }

    private AuthorizationFilterContext Context()
    {
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
    }
}
