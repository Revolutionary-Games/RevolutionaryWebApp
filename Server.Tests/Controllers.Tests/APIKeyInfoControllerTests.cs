namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Server.Authorization;
using Server.Controllers;
using Server.Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class APIKeyInfoControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsRequestsWithoutAPIKey(bool browserSession)
    {
        var context = new DefaultHttpContext();
        if (browserSession)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            context.Items[AppInfo.CurrentUserMiddlewareKey] = new User("user@example.com", "user");
            context.Items[AppInfo.CurrentUserSessionMiddleWareKey] = new Session();
        }

        var controller = new APIKeyInfoController
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        Assert.IsType<UnauthorizedResult>(controller.Get().Result);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("24", false)]
    [InlineData("2,7", true)]
    public void ReturnsCurrentKeyAndOwnerForAnyScope(string scopes, bool readOnly)
    {
        var user = new User("user@example.com", "user") { Id = 42 };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.Developer, GroupType.PatreonSupporter));
        var key = new APIKey
        {
            Id = "abcdefghijklmn",
            Secret = Guid.NewGuid().ToString(),
            User = user,
            UserId = user.Id,
            Scopes = scopes,
            ReadOnly = readOnly,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        var scope = Assert.Single(typeof(APIKeyInfoController).GetCustomAttributes(typeof(APIKeyScopeAttribute), true));
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(scope), "API key info"));
        Assert.True(APIKeyAuthorization.AllowsRequest(key, context, user.AccessCachedGroupsOrThrow()));
        context.Items[AppInfo.AuthenticationKeyMiddleWareKey] = key;
        var controller = new APIKeyInfoController
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = Assert.IsType<APIKeyInfo>(controller.Get().Value);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal(user.AccessCachedGroupsOrThrow().Groups, result.Groups);
        Assert.Equal(key.ExpiresAt, result.Key.ExpiresAt);
        Assert.Equal(readOnly, result.Key.ReadOnly);
        Assert.Equal(key.GetDTO().Scopes, result.Key.Scopes);
        Assert.DoesNotContain(key.Secret, JsonSerializer.Serialize(result));
        Assert.Equal("no-store", context.Response.Headers.CacheControl);

        key.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        Assert.False(APIKeyAuthorization.AllowsRequest(key, context, user.AccessCachedGroupsOrThrow()));
    }
}
