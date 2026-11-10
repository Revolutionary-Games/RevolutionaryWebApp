namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Applies MVC authorization filters before output-cache lookups. Lifetimes and query variation remain controlled
///   by the endpoint's output-cache configuration. Used by the opt-in "Authenticated" policy for endpoints
///   that return the same response to all callers passing their access attributes.
/// </summary>
public sealed class AuthorizationOutputCachePolicy : IOutputCachePolicy
{
    private static readonly object CacheDeniedKey = new();

    public async ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var endpoint = http.GetEndpoint();
        var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var filters = action != null ?
            action.FilterDescriptors.Select(f => f.Filter).ToList() :
            endpoint?.Metadata.GetOrderedMetadata<IFilterMetadata>().ToList() ?? new List<IFilterMetadata>();

        var actionContext = new ActionContext(http, http.GetRouteData(), action ?? new ActionDescriptor());
        if (filters.Any(f => f is IFilterFactory))
        {
            var items = action != null ?
                action.FilterDescriptors.Select(f => new FilterItem(f)).ToList() :
                filters.Select(f => new FilterItem(new FilterDescriptor(f, 0))).ToList();
            var providerContext = new FilterProviderContext(actionContext, items);
            var providers = http.RequestServices.GetServices<IFilterProvider>().OrderBy(p => p.Order).ToArray();
            foreach (var provider in providers)
                provider.OnProvidersExecuting(providerContext);
            foreach (var provider in providers.Reverse())
                provider.OnProvidersExecuted(providerContext);
            filters = providerContext.Results.Where(f => f.Filter != null).Select(f => f.Filter!).ToList();
        }

        bool allowed = true;
        var authorization = new AuthorizationFilterContext(actionContext, filters);
        foreach (var filter in filters.OrderBy(f => (f as IOrderedFilter)?.Order ?? 0))
        {
            if (filter is IAsyncAuthorizationFilter asyncFilter)
            {
                await asyncFilter.OnAuthorizationAsync(authorization);
            }
            else if (filter is IAuthorizationFilter syncFilter)
            {
                syncFilter.OnAuthorization(authorization);
            }

            if (authorization.Result == null)
                continue;

            allowed = false;
            break;
        }

        if (!allowed)
        {
            context.AllowCacheLookup = false;
            context.AllowCacheStorage = false;
            http.Items[CacheDeniedKey] = true;

            // Other composed policies can re-enable lookups. A unique variation also prevents
            // them from finding a cached success, leaving MVC to produce its normal authorization response.
            // Final prevention happens in ServeResponseAsync.
            context.CacheVaryByRules.VaryByValues["authorization-denied"] = Guid.NewGuid().ToString("N");
            return;
        }

        // Authorized callers share responses. Endpoints with per-user output or custom access checks in the action
        // must not use this policy: cached hits bypass the action.
        // Access keys remain separate; API-key scope checks run in authentication middleware before output caching.
        context.CacheVaryByRules.VaryByValues["access-key"] = http.AuthenticatedAccessKey()?.Id.ToString() ?? "none";
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        if (context.HttpContext.Items.ContainsKey(CacheDeniedKey) ||
            context.HttpContext.Response.StatusCode != StatusCodes.Status200OK ||
            context.HttpContext.Response.Headers.ContainsKey("Set-Cookie"))
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
