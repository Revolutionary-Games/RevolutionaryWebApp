namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Models;
    using Shared;

    [AttributeUsage(AttributeTargets.Method)]
    public class AuthorizeRoleFilterAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public UserAccessLevel RequiredAccess { get; set; } = UserAccessLevel.User;
        public AuthenticationScopeRestriction? RequiredRestriction { get; set; } = AuthenticationScopeRestriction.None;

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.HttpContext.User.Identity == null ||
                !context.HttpContext.Items.TryGetValue(AppInfo.CurrentUserMiddleWareKey, out object rawUser))
            {
                context.Result = new UnauthorizedResult();
                return Task.CompletedTask;
            }

            var user = rawUser as User;

            if (user == null || !user.HasAccessLevel(RequiredAccess))
            {
                context.Result = new ForbidResult();
                return Task.CompletedTask;
            }

            if (RequiredRestriction != null)
            {
                if (!context.HttpContext.Items.TryGetValue("AuthenticatedUserScopeRestriction",
                    out object restrictionRaw))
                    throw new InvalidOperationException("authentication scope restriction was not set");

                var restriction = (AuthenticationScopeRestriction)restrictionRaw;

                if (restriction != RequiredRestriction)
                {
                    context.Result = new ForbidResult();
                    return Task.CompletedTask;
                }
            }

            return Task.CompletedTask;
        }
    }
}
