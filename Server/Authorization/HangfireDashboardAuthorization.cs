namespace ThriveDevCenter.Server.Authorization;

using System.Net;
using Hangfire.Dashboard;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;

public class HangfireDashboardAuthorization : IDashboardAuthorizationFilter
{
    private readonly ILogger<HangfireDashboardAuthorization> logger;

    public HangfireDashboardAuthorization(ILogger<HangfireDashboardAuthorization> logger)
    {
        this.logger = logger;
    }

    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        var remoteAddress = http.Connection.RemoteIpAddress;

        // Separate authentication handler should have already set user for us
        if (!http.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object? userRaw) || userRaw == null)
        {
            OnFailure(remoteAddress);
            return false;
        }

        var user = userRaw as User;

        if (user == null || user.Suspended == true || !user.HasAccessLevel(UserAccessLevel.Admin))
        {
            OnFailure(remoteAddress);
            return false;
        }

        return true;
    }

    private void OnFailure(IPAddress? remoteIpdAddress)
    {
        logger.LogWarning(
            "Client from {RemoteIpAddress} tried to access hangfire dashboard without authorization",
            remoteIpdAddress);
    }
}