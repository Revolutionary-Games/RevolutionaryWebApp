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

        // TODO: find out why this is not used
        public const string SecondPrecisionDurationFormat = @"hh\:mm\:ss";

        public const string GitLfsContentType = "application/vnd.git-lfs+json";
        public const string GithubApiContentType = "application/vnd.github.v3+json";

        public const string CIConfigurationFile = "CIConfiguration.yml";
        public const string NoCommitHash = "0000000000000000000000000000000000000000";

        public const string CSRFNeededName = "CSRFRequired";
        public const string CSRFStatusName = "CSRF";
        public const string CurrentUserMiddlewareKey = "AuthenticatedUser";
        public const string CurrentUserSessionMiddleWareKey = "AuthenticatedUserSessionIfBrowser";
        public const string AccessKeyMiddlewareKey = "UsedAccessKey";
        public const string LauncherLinkMiddlewareKey = "UsedLauncherLink";

        public const string LocalStorageUserInfo = "LastPageLoadUser";

        public const string SoftDeleteAttribute = "Deleted";

        public const string MarkdownMimeType = "text/markdown; charset=UTF-8";
        public const string TarXZMimeType = "application/x-xz";
        public const string TarGZMimeType = "application/gzip";

        public const int APITokenByteCount = 34;
        public const int SsoNonceLength = 32;

        public const int PartialEmailMatchRevealAfterLenght = 15;
        public const int PartialGithubMatchRevealAfterLenght = 8;

        public const int MaxDehydratedObjectsPerOffer = 100;
        public const int MaxDehydratedObjectsInDevBuild = 5000;
        public const int MaxPageSizeForBuildSearch = 100;
        public const int MaxDehydratedDownloadBatch = 100;

        public const int MaxDevBuildDescriptionLength = 4000;
        public const int MinimumDevBuildDescriptionLength = 20;
        public const int MaxDevBuildDescriptionNiceLineLength = 70;

        public const int MinEmailLength = 3;
        public const int MaxEmailLength = 250;
        public const int PersonsNameMaximumLength = 500;

        public const int MinPasswordLength = 6;
        public const int MaxPasswordLength = 250;

        public const int MinUsernameLength = 2;
        public const int MaxUsernameLength = 100;

        public const int MaximumTokenLength = 10000;

        public const int MinimumReportTextSearchLength = 3;

        public const int KIBIBYTE = 1024;
        public const int MEBIBYTE = KIBIBYTE * KIBIBYTE;
        public const int GIBIBYTE = MEBIBYTE * 1024;

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
        ///   Maximum size of a file uploaded to the general file storage by a client.
        ///   Note that if this is increased over this value, multipart uploads need to go directly to the final
        ///   storage path (as they can't be copied)
        /// </summary>
        public const long MaxGeneralFileStoreSize = 4024L * MEBIBYTE;

        public const long FileSizeBeforeMultipartUpload = 70 * MEBIBYTE;
        public const long MultipartUploadChunkSize = 50 * MEBIBYTE;
        public const long MultipartUploadChunkSizeLarge = 100 * MEBIBYTE;
        public const long MultipartUploadChunkSizeLargeThreshold = 1 * GIBIBYTE;
        public const int MultipartSimultaneousUploads = 3;
        public const int MultipartUploadPartsToReturnInSingleCall = 5;

        public const int MaxInBrowserPreviewTextFileSize = MEBIBYTE * 20;
        public const int MaxSingleBuildOutputMessageLength = MEBIBYTE * 20;

        public const long MaxCrashDumpUploadSize = MEBIBYTE * 9;
        public const long MaxCrashLogsLength = MEBIBYTE * 5;

        public const long MaxDebugSymbolSize = MEBIBYTE * 500;

        public const int MaxBuildOutputLineLength = 4000;

        public const int MaxBulkEmailsPerInterval = 4;
        public const int MaxBulkEmailDelaySeconds = 3600;
        public const int BulkEmailChunkSize = 10;

        public const int MaxBulkCLAChecksPerCall = 250;

        /// <summary>
        ///   Sessions (and cookies) expire after 30 days of inactivity
        /// </summary>
        public const int SessionExpirySeconds = 60 * 60 * 24 * 30;

        /// <summary>
        ///   Cookies expire 60 days after creation as there is no refresh mechanism this is set higher
        ///   than the session expiry
        /// </summary>
        public const int ClientCookieExpirySeconds = 60 * 60 * 24 * 60;

        public const int CrashDumpDumpFileRetentionDays = 90;
        public const int MaximumDuplicateReports = 1000;

        public const int DefaultTableNotificationFetchTimer = 1000;
        public const int LongerTableNotificationFetchTimer = 5000;
        public const int LongestTableNotificationFetchTimer = 30000;
        public const int LongerTableRefreshIntervalCutoff = 4;
        public const int LongestTableRefreshIntervalCutoff = 11;

        public const long SingleResourceTableRowId = 1;
        public const int MinExternalServerPriority = -10;
        public const int MaxExternalServerPriority = 10;

        public const int SshServerCommandAttempts = 3;

        public const int Major = 1;
        public const int Minor = 12;

        public const int DefaultMaxLauncherLinks = 5;

        public const int MinimumRedeemableCodeLength = 8;

        public const int DefaultDatabaseUpdateFailureAttempts = 100;

        public const int UsernameRetrieveBatchSize = 50;

        public const int MaxDebugSymbolOfferBatch = 50;

        /// <summary>
        ///   Maximum size of our normal caches combined across all controllers (in bytes)
        /// </summary>
        public const int MaxNormalCacheSize = 150 * MEBIBYTE;

        /// <summary>
        ///   The interval in seconds that a session use is updated to the database
        /// </summary>
        public static readonly TimeSpan LastUsedSessionAccuracy = TimeSpan.FromSeconds(60);

        public static readonly TimeSpan LastUsedAccessKeyAccuracy = TimeSpan.FromSeconds(60);

        /// <summary>
        ///   How long the token is valid to upload to the general remote storage
        /// </summary>
        public static readonly TimeSpan RemoteStorageUploadExpireTime = TimeSpan.FromMinutes(60);

        public static readonly TimeSpan MultipartUploadTotalAllowedTime = TimeSpan.FromHours(4);

        public static readonly TimeSpan RemoteStorageDownloadExpireTime = TimeSpan.FromMinutes(15);

        public static readonly TimeSpan LauncherLinkCodeExpireTime = TimeSpan.FromMinutes(15);

        public static readonly TimeSpan StartedSigningTimeout = TimeSpan.FromHours(4);

        public static readonly TimeSpan BulkEmailRateLimitInterval = TimeSpan.FromDays(1);

        public static readonly TimeSpan DeleteAbandonedInProgressSignaturesAfter = TimeSpan.FromDays(2);

        public static readonly TimeSpan OldMultipartUploadThreshold = TimeSpan.FromDays(30);

        public static readonly TimeSpan WaitBeforeNameRetrieveBatchStart = TimeSpan.FromMilliseconds(100);

        public static readonly TimeSpan TimeBeforeShowingConnectionLoss = TimeSpan.FromMilliseconds(700);

        public static readonly TimeSpan KeepStackwalkResultsFor = TimeSpan.FromHours(1);

        public static readonly TimeSpan DeleteFailedStackwalkAttemptsAfter = TimeSpan.FromHours(8);

        public static readonly TimeSpan InactiveSymbolKeepDuration = TimeSpan.FromDays(180);
    }
}
