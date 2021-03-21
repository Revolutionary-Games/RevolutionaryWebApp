namespace ThriveDevCenter.Shared
{
    using System;

    /// <summary>
    ///   Holds the current version of the app, to detect mismatch between the client and the server.
    ///   Increment these numbers when the signalr definitions change or the APIs change
    /// </summary>
    public static class AppInfo
    {
        public const bool UsePrerendering = false;

        public const string SessionCookieName = "ThriveDevSession";

        public const string GitLfsContentType = "application/vnd.git-lfs+json";

        public const string CSRFNeededName = "CSRFRequired";
        public const string CSRFStatusName = "CSRF";
        public const string CurrentUserMiddlewareKey = "AuthenticatedUser";

        public const string LocalStorageUserInfo = "LastPageLoadUser";

        /// <summary>
        ///   Sessions (and cookies) expire after 30 days
        /// </summary>
        public const int SessionExpirySeconds = 60 * 60 * 24 * 30;

        /// <summary>
        ///   The interval in seconds that a session use is updated to the database
        /// </summary>
        public static readonly TimeSpan LastUsedSessionAccuracy = TimeSpan.FromSeconds(60);

        public const int Major = 1;
        public const int Minor = 5;

        public const int DefaultMaxLauncherLinks = 5;

        public const int MinimumRedeemableCodeLength = 8;
    }
}
