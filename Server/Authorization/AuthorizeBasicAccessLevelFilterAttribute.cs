namespace ThriveDevCenter.Server.Authorization;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Shared.Models.Enums;

/// <summary>
///   Verifies that there's an authenticated user with the required access level before the route can be accessed.
///   Unless the access level is set to <see cref="GroupType.NotLoggedIn"/>. All attributes placed on the class and
///   accessed route method are executed.
/// </summary>
/// <remarks>
///   <para>
///     If public access is needed to a single route in a controller class, this attribute can't be placed on the class
///     at all because the class attribute will always be executed before the route specific attributes. The other way
///     around works, you can add more restrictive attributes on methods than on the class.
///   </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AuthorizeBasicAccessLevelFilterAttribute : Attribute, IAsyncAuthorizationFilter
{
    private AuthenticationScopeRestriction? requiredRestriction = AuthenticationScopeRestriction.None;
    public GroupType RequiredAccess { get; set; } = GroupType.User;

    public string? RequiredRestriction
    {
        get => requiredRestriction.ToString();
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                requiredRestriction = null;
                return;
            }

            requiredRestriction = Enum.Parse<AuthenticationScopeRestriction>(value);
        }
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var result =
            context.HttpContext.HasAuthenticatedUserWithAccessLevelExtended(RequiredAccess, requiredRestriction);

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
