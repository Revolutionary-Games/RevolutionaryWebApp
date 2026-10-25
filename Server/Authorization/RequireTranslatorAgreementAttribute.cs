namespace RevolutionaryWebApp.Server.Authorization;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

/// <summary>
///   Requires the acting user to have accepted the translation license and guidelines.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireTranslatorAgreementAttribute : Attribute, IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.AuthenticatedUser();
        if (user == null)
        {
            context.Result = new UnauthorizedResult();
        }
        else if (!user.AcceptedTranslator)
        {
            context.Result = new ObjectResult(
                "You must accept the translation agreement before contributing translations.")
            {
                StatusCode = 403,
            };
        }

        return Task.CompletedTask;
    }
}
