namespace ThriveDevCenter.Server.Tests.Utilities;

using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using Server.Authorization;
using Server.Models;
using Shared;

public static class HttpContextMockHelpers
{
    public static Mock<HttpContext> CreateContextWithUser(User? user, bool? csrf = true,
        AuthenticationScopeRestriction scopeRestriction = AuthenticationScopeRestriction.None)
    {
        ClaimsPrincipal principal;

        var contextItems = new Dictionary<object, object?>();

        if (user != null)
        {
            contextItems.Add(AppInfo.CurrentUserMiddlewareKey, user);
            contextItems.Add(AppInfo.AuthenticationScopeRestrictionMiddleWareKey, scopeRestriction);
            principal = new ClaimsPrincipal(new ClaimsIdentity(user));
        }
        else
        {
            principal = new ClaimsPrincipal();
        }

        if (csrf != null)
        {
            contextItems.Add(AppInfo.CSRFStatusName, csrf.Value);
        }

        var mock = new Mock<HttpContext>();
        mock.Setup(c => c.User).Returns(principal);
        mock.Setup(c => c.Items).Returns(contextItems);

        return mock;
    }
}
