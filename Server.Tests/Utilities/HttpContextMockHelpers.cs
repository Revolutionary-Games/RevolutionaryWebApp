namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NSubstitute.Extensions;
using Server.Authorization;
using Server.Models;
using Shared;

public static class HttpContextMockHelpers
{
    public static HttpContext CreateContextWithUser(User? user, bool? csrf = true,
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

        var mock = Substitute.ForPartsOf<HttpContext>();
        mock.Configure().User.Returns(principal);
        mock.Configure().Items.Returns(contextItems);

        return mock;
    }
}
