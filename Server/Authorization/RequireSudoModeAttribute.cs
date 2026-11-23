namespace RevolutionaryWebApp.Server.Authorization;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Shared;
using Shared.Models;

/// <summary>
///   Requires session-specific sudo mode in addition to the endpoint's ordinary authorization checks.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireSudoModeAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
    // Check permissions first, so sudo challenges never replace an ordinary access denial.
    public int Order => int.MaxValue;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.Result != null)
            return;

        var http = context.HttpContext;
        var user = http.AuthenticatedUser();
        if (user == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var session = http.AuthenticatedUserSession();
        if (session == null)
        {
            context.Result = new ObjectResult(AppInfo.SudoModeWebsiteOnlyMessage)
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        var sudo = http.RequestServices.GetRequiredService<SudoModeService>();
        if (await sudo.IsActive(session, user.Id))
            return;

        var twoFactor = http.RequestServices.GetRequiredService<TwoFactorService>();
        var enabled = (await twoFactor.GetStatus(user.Id)).Enabled;
        http.Response.Headers[AppInfo.SudoModeResponseHeader] = AppInfo.SudoModeRequiredHeaderValue;
        http.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(new SudoModeRequiredDTO
        {
            TwoFactorEnabled = enabled,
            Message = enabled ?
                "Enter sudo mode with a fresh two-factor code to continue." :
                "Set up two-factor authentication in your account settings before performing this operation.",
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
