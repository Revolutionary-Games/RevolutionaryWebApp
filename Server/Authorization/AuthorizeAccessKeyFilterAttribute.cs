namespace RevolutionaryWebApp.Server.Authorization;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Shared.Models;

/// <summary>
///   Verifies that there's an authenticated access key with the required type before the route can be accessed.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AuthorizeAccessKeyFilterAttribute : Attribute, IAsyncAuthorizationFilter
{
    public AccessKeyType RequiredAccess { get; set; } = AccessKeyType.DevBuilds;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var result = context.HttpContext.HasAuthenticatedAccessKeyExtended(RequiredAccess);

        switch (result)
        {
            case HttpContextAuthorizationExtensions.AuthenticationResult.NoUser:
                context.Result = new UnauthorizedResult();
                break;
            case HttpContextAuthorizationExtensions.AuthenticationResult.NoAccess:
                context.Result = new ForbidResult();
                break;
            case HttpContextAuthorizationExtensions.AuthenticationResult.Success:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return Task.CompletedTask;
    }
}
