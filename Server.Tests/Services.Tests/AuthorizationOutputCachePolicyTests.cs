namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Server.Authorization;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models.Enums;
using Xunit;

public class AuthorizationOutputCachePolicyTests
{
    [Theory]
    [InlineData("basic")]
    [InlineData("group")]
    [InlineData("agreement")]
    [InlineData("admin")]
    [InlineData("factory")]
    public async Task CachedMvcActionsRecheckAllAuthorizationAttributes(string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultForbidScheme = "forbidScheme";
            options.AddScheme<MyForbidHandler>("forbidScheme", "Handle Forbidden");
        });
        builder.Services.AddScoped<RequireTranslatorAgreementAttribute>();
        builder.Services.AddControllers().AddApplicationPart(typeof(CachePolicyTestController).Assembly);
        builder.Services.AddOutputCache(options =>
        {
            options.AddPolicy("Authenticated", policy => policy.AddPolicy<AuthorizationOutputCachePolicy>());
            options.AddPolicy("ShortLived", policy => policy.Expire(TimeSpan.FromSeconds(30)));
        });
        await using var app = builder.Build();
        var actor = new User("reader@example.com", "reader") { Id = 1, AcceptedTranslator = true };
        actor.Groups.Add(new UserGroup(GroupType.Admin, "Admin"));
        actor.ProcessGroupDataFromLoadedGroups();
        var revokedActor = new User("reader@example.com", "reader") { Id = 1 };
        revokedActor.ProcessGroupDataFromLoadedGroups();

        app.Use(async (http, next) =>
        {
            if (http.Request.Headers.ContainsKey("Signed-In"))
            {
                http.Items[AppInfo.CurrentUserMiddlewareKey] = http.Request.Headers.ContainsKey("Revoked-Access") ?
                    revokedActor :
                    actor;
                http.Items[AppInfo.AuthenticationScopeRestrictionMiddleWareKey] =
                    http.Request.Headers.ContainsKey("Restricted-Scope") ?
                        AuthenticationScopeRestriction.LFSOnly :
                        AuthenticationScopeRestriction.None;
            }

            await next();
        });
        app.UseRouting();
        app.UseOutputCache();
        app.MapControllers();

        await app.StartAsync();
        using var client = app.GetTestClient();

        client.DefaultRequestHeaders.Add("Signed-In", "true");
        var url = $"/cache-policy-tests/{path}?query=abc";
        var first = await client.GetStringAsync(url);
        Assert.Equal(first, await client.GetStringAsync(url));
        Assert.NotEqual(first, await client.GetStringAsync(url + "d"));

        // Authorized callers share the same response; these endpoints must not return personalized output.
        actor.Id = 2;
        Assert.Equal(first, await client.GetStringAsync(url));
        actor.Id = 1;
        Assert.Equal(first, await client.GetStringAsync(url));

        client.DefaultRequestHeaders.Add("Restricted-Scope", "true");
        using (var response = await client.GetAsync(url))
        {
            Assert.Equal(403, (int)response.StatusCode);
        }

        client.DefaultRequestHeaders.Remove("Restricted-Scope");

        if (path != "basic")
        {
            client.DefaultRequestHeaders.Add("Revoked-Access", "true");
            using var response = await client.GetAsync(url);
            Assert.Equal(403, (int)response.StatusCode);
        }

        client.DefaultRequestHeaders.Remove("Signed-In");
        using var anonymous = await client.GetAsync(url);
        Assert.Equal(401, (int)anonymous.StatusCode);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("short-lived")]
    public async Task PublicCachesOnlyCreateAndRunAuthorizationFiltersOnCacheMisses(string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(PublicCachePolicyTestController).Assembly);
        builder.Services.AddSingleton<CacheFilterCounts>();
        builder.Services.AddTransient<CountingCacheAuthorizationFilter>();
        builder.Services.AddOutputCache(options =>
        {
            options.AddPolicy("Authenticated", policy => policy.AddPolicy<AuthorizationOutputCachePolicy>());
            options.AddPolicy("ShortLived", policy => policy.Expire(TimeSpan.FromSeconds(30)));
        });
        await using var app = builder.Build();
        app.UseRouting();
        app.UseOutputCache();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var counts = app.Services.GetRequiredService<CacheFilterCounts>();
        var url = $"/public-cache-policy-tests/{path}?query=abc";

        var first = await client.GetStringAsync(url);
        Assert.Equal(1, counts.Created);
        Assert.Equal(1, counts.Executed);
        Assert.Equal(first, await client.GetStringAsync(url));
        Assert.Equal(first, await client.GetStringAsync(url));
        Assert.Equal(1, counts.Created);
        Assert.Equal(1, counts.Executed);

        // A different query misses the cache and must run the normal MVC authorization pipeline again.
        Assert.NotEqual(first, await client.GetStringAsync(url + "d"));
        Assert.Equal(2, counts.Created);
        Assert.Equal(2, counts.Executed);
    }

    [Fact]
    public async Task GroupFallbackAndAnonymousPublicEndpointsRetainTheirSemantics()
    {
        var actor = new User("reader@example.com", "reader");
        actor.Groups.Add(new UserGroup(GroupType.DictionaryManager, "Dictionary manager"));
        actor.ProcessGroupDataFromLoadedGroups();
        var http = new DefaultHttpContext();
        http.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        http.Items[AppInfo.AuthenticationScopeRestrictionMiddleWareKey] = AuthenticationScopeRestriction.None;
        var filter = new AuthorizeGroupMemberFilterAttribute
        {
            RequiredGroup = GroupType.ContentModerator, AllowFallbackGroup = GroupType.DictionaryManager,
        };
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(filter), "test"));
        var context = new OutputCacheContext { HttpContext = http, AllowCacheLookup = true, AllowCacheStorage = true };
        var policy = new AuthorizationOutputCachePolicy();
        await policy.CacheRequestAsync(context, CancellationToken.None);
        Assert.True(context.AllowCacheLookup);
        Assert.True(context.AllowCacheStorage);

        var anonymous = new OutputCacheContext
        {
            HttpContext = new DefaultHttpContext(), AllowCacheLookup = true, AllowCacheStorage = true,
        };
        await policy.CacheRequestAsync(anonymous, CancellationToken.None);
        Assert.True(anonymous.AllowCacheLookup);
        Assert.False(anonymous.CacheVaryByRules.VaryByValues.ContainsKey("user"));
        Assert.False(anonymous.CacheVaryByRules.VaryByValues.ContainsKey("session"));
        Assert.False(anonymous.CacheVaryByRules.VaryByValues.ContainsKey("scope"));
        Assert.Equal("none", anonymous.CacheVaryByRules.VaryByValues["access-key"]);
        anonymous.HttpContext.Response.Headers.SetCookie = "session=test";
        await policy.ServeResponseAsync(anonymous, CancellationToken.None);
        Assert.False(anonymous.AllowCacheStorage);
    }
}

[ApiController]
[Route("cache-policy-tests")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
public class CachePolicyTestController : ControllerBase
{
    [HttpGet("basic")]
    [OutputCache(PolicyName = "Authenticated")]
    public string Basic()
    {
        return Guid.NewGuid().ToString();
    }

    [HttpGet("group")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.ContentModerator, AllowAdmin = true)]
    [OutputCache(PolicyName = "Authenticated")]
    public string Group()
    {
        return Guid.NewGuid().ToString();
    }

    [HttpGet("agreement")]
    [RequireTranslatorAgreement]
    [OutputCache(PolicyName = "Authenticated", Duration = 45)]
    public string Agreement()
    {
        return Guid.NewGuid().ToString();
    }

    [HttpGet("factory")]
    [ServiceFilter(typeof(RequireTranslatorAgreementAttribute))]
    [OutputCache(PolicyName = "Authenticated")]
    public string Factory()
    {
        return Guid.NewGuid().ToString();
    }

    [HttpGet("admin")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [OutputCache(PolicyName = "Authenticated", Duration = 45)]
    public string Admin()
    {
        return Guid.NewGuid().ToString();
    }
}

[ApiController]
[Route("public-cache-policy-tests")]
[ServiceFilter(typeof(CountingCacheAuthorizationFilter))]
public class PublicCachePolicyTestController : ControllerBase
{
    [HttpGet("default")]
    [OutputCache(Duration = 45)]
    public string Default()
    {
        return Guid.NewGuid().ToString();
    }

    [HttpGet("short-lived")]
    [OutputCache(PolicyName = "ShortLived")]
    public string ShortLived()
    {
        return Guid.NewGuid().ToString();
    }
}

public class CacheFilterCounts
{
    public int Created { get; set; }
    public int Executed { get; set; }
}

public class CountingCacheAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly CacheFilterCounts counts;

    public CountingCacheAuthorizationFilter(CacheFilterCounts counts)
    {
        this.counts = counts;
        ++counts.Created;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ++counts.Executed;
        return Task.CompletedTask;
    }
}
