namespace ThriveDevCenter.Shared
{
    using System;

    /// <summary>
    ///   Holds App-wide constant values.
    ///   Contains the current version of the app, to detect mismatch between the client and the server.
    ///   Increment these numbers when the signalr definitions change or the APIs change.
    /// </summary>
    public static class AppInfo
    {
        public const bool UsePrerendering = false;

        public const string SessionCookieName = "ThriveDevSession";

        public const string GitLfsContentType = "application/vnd.git-lfs+json";

        public const string CSRFNeededName = "CSRFRequired";
        public const string CSRFStatusName = "CSRF";
        public const string CurrentUserMiddlewareKey = "AuthenticatedUser";
        public const string AccessKeyMiddlewareKey = "UsedAccessKey";

        public const string LocalStorageUserInfo = "LastPageLoadUser";

        public const string ItemTypeFolder = "folder";
        public const string ItemTypeFile = "file";

        public const string SoftDeleteAttribute = "Deleted";

        public const int APITokenByteCount = 34;

        public const int MaxDehydratedObjectsPerOffer = 100;
        public const int MaxDehydratedObjectsInDevBuild = 5000;

        public const int KIBIBYTE = 1024;
        public const int MEBIBYTE = KIBIBYTE * KIBIBYTE;

        /// <summary>
        ///   Maximum size of a file to upload through LFS
        /// </summary>
        public const long MaxLfsUploadSize = 75 * MEBIBYTE;

        /// <summary>
        ///   Maximum size of a dehydrated file
        /// </summary>
        public const long MaxDehydratedUploadSize = 200 * MEBIBYTE;

        /// <summary>
        ///   Maximum size of a devbuild file
        /// </summary>
        public const long MaxDevBuildUploadSize = 50 * MEBIBYTE;

        /// <summary>
        ///   Sessions (and cookies) expire after 30 days of inactivity
        /// </summary>
        public const int SessionExpirySeconds = 60 * 60 * 24 * 30;

        /// <summary>
        ///   Cookies expire 60 days after creation as there is no refresh mechanism this is set higher
        ///   than the session expiry
        /// </summary>
        public const int ClientCookieExpirySeconds = 60 * 60 * 24 * 60;

        public const int DefaultTableNotificationFetchTimer = 1000;
        public const int LongerTableNotificationFetchTimer = 5000;
        public const int LongestTableNotificationFetchTimer = 30000;
        public const int LongerTableRefreshIntervalCutoff = 4;
        public const int LongestTableRefreshIntervalCutoff = 11;

        /// <summary>
        ///   The interval in seconds that a session use is updated to the database
        /// </summary>
        public static readonly TimeSpan LastUsedSessionAccuracy = TimeSpan.FromSeconds(60);

        public static readonly TimeSpan LastUsedAccessKeyAccuracy = TimeSpan.FromSeconds(60);

        public const int Major = 1;
        public const int Minor = 7;

        public const int DefaultMaxLauncherLinks = 5;

        public const int MinimumRedeemableCodeLength = 8;
    }
}
