namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

public abstract class SSOLoginController : Controller
{
    private static readonly TimeSpan SsoTimeout = TimeSpan.FromMinutes(20);

    protected readonly ILogger<SSOLoginController> Logger;
    protected readonly NotificationsEnabledDb Database;

    protected SSOLoginController(ILogger<SSOLoginController> logger, NotificationsEnabledDb database)
    {
        Logger = logger;
        Database = database;
    }

    protected static void ClearSSOParametersFromSession(Session session)
    {
        session.StartedSsoLogin = null;

        // Clear the return url to not leave it hanging around in the database
        session.SsoReturnUrl = null;
    }

    protected void SetupSessionForSSO(string ssoSource, string? returnTo, Session session)
    {
        session.LastUsed = DateTime.UtcNow;

        var remoteAddress = Request.HttpContext.Connection.RemoteIpAddress;
        session.LastUsedFrom = remoteAddress;
        session.SsoNonce = NonceGenerator.GenerateNonce(AppInfo.SsoNonceLength);
        session.StartedSsoLogin = ssoSource;
        session.SsoStartTime = DateTime.UtcNow;
        session.SsoReturnUrl = returnTo;
    }

    protected async Task<(Session? session, IActionResult? result)> FetchAndCheckSessionForSsoReturn(string? nonce,
        string ssoType)
    {
        var session = await HttpContext.Request.Cookies.GetSession(Database);

        if (session == null || session.StartedSsoLogin != ssoType)
        {
            return (session, Redirect(QueryHelpers.AddQueryString("/login", "error",
                "Your session was invalid. Please try again.")));
        }

        if (IsSsoTimedOut(session, out IActionResult? timedOut))
            return (session, timedOut);

        var remoteAddress = Request.HttpContext.Connection.RemoteIpAddress;

        if (session.LastUsedFrom == null)
        {
            return (null, Redirect(QueryHelpers.AddQueryString("/login", "error",
                "Your IP address when starting SSO login was not stored correctly.")));
        }

        // Maybe this offers some extra security
        if (!session.LastUsedFrom.Equals(remoteAddress))
        {
            return (session, Redirect(QueryHelpers.AddQueryString("/login", "error",
                "Your IP address changed during the login attempt.")));
        }

        if (nonce == null || session.SsoNonce != nonce || string.IsNullOrEmpty(session.SsoNonce))
        {
            return (session, Redirect(QueryHelpers.AddQueryString("/login", "error",
                "Invalid request nonce. Please try again.")));
        }

        // Clear nonce after checking to disallow duplicate requests (need to make sure to save in code
        // calling this method)
        session.SsoNonce = null;
        return (session, null);
    }

    [NonAction]
    protected IActionResult GetInvalidSsoParametersResult()
    {
        return Redirect(QueryHelpers.AddQueryString("/login", "error",
            "Invalid SSO parameters received"));
    }

    [NonAction]
    private bool IsSsoTimedOut(Session session, out IActionResult? result)
    {
        if (session.SsoStartTime == null || DateTime.UtcNow - session.SsoStartTime > SsoTimeout)
        {
            result = Redirect(QueryHelpers.AddQueryString("/login", "error",
                "The login attempt has expired. Please try again."));
            return true;
        }

        result = null;
        return false;
    }
}
