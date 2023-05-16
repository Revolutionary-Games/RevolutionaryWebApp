namespace ThriveDevCenter.Server.Authorization;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Shared.Models.Enums;

/// <summary>
///   Verifies that there's an authenticated user with the required group before the route can be accessed.
///   Basic levels like <see cref="GroupType.NotLoggedIn"/> <see cref="GroupType.RestrictedUser"/> are not supported.
///   Other predefined groups are supported. This can be placed on a class to protect all methods in it, with
///   optionally placing extra attributes on method to further restrict things.
/// </summary>
/// <remarks>
///   <para>
///     See the remark in <see cref="AuthorizeBasicAccessLevelFilterAttribute"/> about how to design classes that have
///     at least one publicly accessible method.
///   </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AuthorizeGroupMemberFilterAttribute : Attribute, IAsyncAuthorizationFilter
{
    private AuthenticationScopeRestriction? requiredRestriction = AuthenticationScopeRestriction.None;
    private GroupType requiredGroup = GroupType.User;

    public GroupType RequiredGroup
    {
        get => requiredGroup;
        set
        {
            requiredGroup = value;

            if (requiredGroup is GroupType.RestrictedUser or GroupType.NotLoggedIn)
            {
                throw new ArgumentException(
                    $"This group filter doesn't support the group type: {RequiredGroup}, " +
                    "please use the access level filter");
            }
        }
    }

    /// <inheritdoc cref="AuthorizeBasicAccessLevelFilterAttribute.RequiredRestriction"/>
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
            context.HttpContext.HasAuthenticatedUserWithGroupExtended(RequiredGroup, requiredRestriction);

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
