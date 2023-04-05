namespace ThriveDevCenter.Server.Authorization;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Shared.Models.Enums;

[AttributeUsage(AttributeTargets.Method)]
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
