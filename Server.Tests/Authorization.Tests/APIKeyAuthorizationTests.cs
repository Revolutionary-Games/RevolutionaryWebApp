namespace RevolutionaryWebApp.Server.Tests.Authorization.Tests;

using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Authorization;
using Server.Models;
using Server.Services;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class APIKeyAuthorizationTests
{
    [Theory]
    [InlineData("GET", true, true)]
    [InlineData("POST", true, false)]
    [InlineData("DELETE", true, false)]
    [InlineData("HEAD", true, false)]
    [InlineData("PUT", false, true)]
    public void ReadOnlyEnforcesMethod(string method, bool readOnly, bool expected)
    {
        var key = Key();
        key.ReadOnly = readOnly;
        Assert.Equal(expected, APIKeyAuthorization.AllowsRequest(key, Context(method),
            new CachedUserGroups(GroupType.Developer)));
    }

    [Theory]
    [InlineData(true, "GET", false)]
    [InlineData(false, "POST", true)]
    public void WriteAttributeOverridesMethod(bool writes, string method, bool expected)
    {
        var context = Context(method, new APIKeyWriteAttribute(writes));
        Assert.Equal(expected, APIKeyAuthorization.AllowsRequest(Key(), context,
            new CachedUserGroups(GroupType.Developer)));
    }

    [Fact]
    public void ScopesAndAllAreEnforced()
    {
        var key = Key();
        key.Scopes = "2,7";
        Assert.True(key.AllowsScope(APIKeyScope.Translations));
        Assert.False(key.AllowsScope(APIKeyScope.Users));
        key.Scopes = "0";
        foreach (var scope in Enum.GetValues<APIKeyScope>())
            Assert.True(key.AllowsScope(scope));
        key.Scopes = "2";
        Assert.False(APIKeyAuthorization.AllowsRequest(key, Context("GET"), new CachedUserGroups(GroupType.User)));
    }

    [Theory]
    [InlineData("192.0.2.0/24", "192.0.2.255", true)]
    [InlineData("192.0.2.0/24", "192.0.3.0", false)]
    [InlineData("192.0.2.0/24", "::ffff:192.0.2.1", true)]
    [InlineData("2001:db8::/32", "2001:db8::1", true)]
    [InlineData("2001:db8::/32", "2001:db9::1", false)]
    [InlineData("192.0.2.1/32", "192.0.2.2", false)]
    public void IPRestrictionsCoverIPv4AndIPv6(string network, string address, bool expected)
    {
        var key = Key();
        key.AllowedIPNetwork = network;
        Assert.Equal(expected, key.AllowsIP(IPAddress.Parse(address)));
        Assert.False(key.AllowsIP(null));
    }

    [Fact]
    public void DisabledOwnersExpirationAndMissingMetadataDenyAccess()
    {
        var key = Key();
        var groups = new CachedUserGroups(GroupType.User);
        Assert.True(APIKeyAuthorization.AllowsRequest(key, Context("GET"), groups));
        key.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        Assert.False(APIKeyAuthorization.AllowsRequest(key, Context("GET"), groups));
        key.ExpiresAt = null;
        Assert.False(APIKeyAuthorization.AllowsRequest(key, Context("GET"),
            new CachedUserGroups(GroupType.RestrictedUser)));
        Assert.False(APIKeyAuthorization.AllowsRequest(key, new DefaultHttpContext(), groups));
    }

    [Theory]
    [InlineData(GroupType.User, 1, false)]
    [InlineData(GroupType.PatreonSupporter, 3, true)]
    [InlineData(GroupType.Developer, 5, true)]
    [InlineData(GroupType.Admin, 20, true)]
    [InlineData(GroupType.RestrictedUser, 0, false)]
    public void AccountLimitsAndWritePrivileges(GroupType group, int limit, bool canWrite)
    {
        var groups = new CachedUserGroups(group);
        Assert.Equal(limit, APIKeyPolicy.Limit(groups));
        var key = Key();
        key.ReadOnly = false;
        Assert.Equal(canWrite, APIKeyAuthorization.AllowsRequest(key, Context("POST"), groups));
    }

    [Fact]
    public void SecretIsNeverInListOrModelJson()
    {
        var key = Key();
        Assert.DoesNotContain(key.Secret, JsonSerializer.Serialize(key.GetDTO()));
        Assert.DoesNotContain(key.Secret, JsonSerializer.Serialize(key));
    }

    [Theory]
    [InlineData("2", true, GroupType.Developer, true, true)]
    [InlineData("2", false, GroupType.Developer, true, false)]
    [InlineData("0", true, GroupType.Developer, false, true)]
    [InlineData("0", false, GroupType.User, false, true)]
    public async Task ForbiddenResponseExplainsScopeAndReadOnlyFailures(string scopes, bool readOnly,
        GroupType group, bool scopeError, bool readOnlyError)
    {
        using var fixture = new EditableInMemoryDatabaseFixture(Guid.NewGuid().ToString());
        var key = Key();
        key.Scopes = scopes;
        key.ReadOnly = readOnly;
        key.User.ForceResolveGroupsForTesting(new CachedUserGroups(group));
        fixture.Database.APIKeys.Add(key);
        await fixture.Database.SaveChangesAsync();

        var context = Context("POST");
        context.Request.Headers.Authorization = $"key:{key.Id}:{key.Secret}";
        using var body = new MemoryStream();
        context.Response.Body = body;
        var cache = new CustomMemoryCache();
        using var cacheLifetime = cache.Cache;
        var middleware = new TokenOrCookieAuthenticationMiddleware(
            NullLogger<TokenOrCookieAuthenticationMiddleware>.Instance, fixture.Database, cache);
        var nextCalled = false;
        await middleware.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", context.Response.ContentType);
        body.Position = 0;
        using var reader = new StreamReader(body);
        var message = await reader.ReadToEndAsync();
        Assert.Equal(scopeError, message.Contains("incorrect scopes"));
        Assert.Equal(readOnlyError, message.Contains("read-only"));
        if (scopeError)
            Assert.Contains("Users (1)", message);
        Assert.DoesNotContain(key.Secret, message);
        Assert.Equal(0, key.TotalUses);
    }

    private static APIKey Key()
    {
        return new APIKey
        {
            // ReSharper disable once StringLiteralTypo
            Id = "abcdefghijklmn",
            Secret = Guid.NewGuid().ToString(),
            User = new User("api@example.com", "api"),
        };
    }

    private static DefaultHttpContext Context(string method, APIKeyWriteAttribute? writes = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        var metadata = writes == null ?
            new EndpointMetadataCollection(new APIKeyScopeAttribute(APIKeyScope.Users)) :
            new EndpointMetadataCollection(new APIKeyScopeAttribute(APIKeyScope.Users), writes);
        context.SetEndpoint(new Endpoint(null, metadata, "API key test"));
        return context;
    }
}
