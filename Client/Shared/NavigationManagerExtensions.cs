namespace RevolutionaryWebApp.Client.Shared;

using System;
using Microsoft.AspNetCore.Components;

public static class NavigationManagerExtensions
{
    public const string PermissionProblemErrorMessage = "You don't have the necessary permissions to view this page";
    public const string LoginRequiredErrorMessage = "You need to login to view this page";
    public const string ResourcePrivateErrorMessage = "This resource may exist but is private. " +
        "Logging in may make it available.";

    public static string GetLinkToLogin(this NavigationManager navigationManager, string? message = null)
    {
        var current = Uri.EscapeDataString(navigationManager.Uri);

        if (message != null)
        {
            var escapedMessage = Uri.EscapeDataString(message);
            return $"login?returnUrl={current}&error={escapedMessage}";
        }

        return $"login?returnUrl={current}";
    }

    public static void ForceReload(this NavigationManager navigationManager)
    {
        navigationManager.NavigateTo(navigationManager.Uri, true);
    }

    public static string RelativeUrl(this NavigationManager navigationManager)
    {
        if (!navigationManager.Uri.StartsWith(navigationManager.BaseUri))
            return navigationManager.Uri;

        return navigationManager.Uri.Substring(navigationManager.BaseUri.Length);
    }
}
