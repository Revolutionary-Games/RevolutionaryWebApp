namespace ThriveDevCenter.Shared
{
    /// <summary>
    ///   Holds the current version of the app, to detect mismatch between the client and the server.
    ///   Increment these numbers when the signalr definitions change or the APIs change
    /// </summary>
    public static class AppInfo
    {
        public const bool UsePrerendering = false;

        public const string SessionCookieName = "ThriveDevSession";

        public const string GitLfsContentType = "application/vnd.git-lfs+json";

        public const string CSRFStatusName = "CSRF";

        public static int Major => 1;
        public static int Minor => 5;
    }
}
