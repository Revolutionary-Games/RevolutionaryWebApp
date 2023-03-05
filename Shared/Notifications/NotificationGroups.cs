namespace ThriveDevCenter.Shared.Notifications;

/// <summary>
///   Lists the names of notification groups (or in case of dynamically generated ones, the name prefix).
///   TODO: maybe there is a better place to put the authorization checks than in the hub directly
/// </summary>
public static class NotificationGroups
{
    public const string LFSListUpdated = "LFS";
    public const string PrivateLFSUpdated = "LFS_Developer";
    public const string LFSItemUpdatedPrefix = "SLFS_";

    public const string UserListUpdated = "Users";
    public const string UserUpdatedPrefix = "SUser_";
    public const string UserUpdatedPrefixAdminInfo = "SAUser_";
    public const string UserSessionsUpdatedPrefix = "SUserSession_";

    public const string CLAListUpdated = "CLAs";
    public const string CLAUpdatedPrefix = "CLA_";

    // CI related groups
    public const string CIProjectListUpdated = "CIProjects";
    public const string PrivateCIProjectUpdated = "CIProject_Developer";
    public const string CIProjectUpdatedPrefix = "SCIProject_";

    public const string CIProjectBuildsUpdatedPrefix = "CIProjectBuilds_";
    public const string CIProjectsBuildUpdatedPrefix = "CIProjectBuild_";

    public const string CIProjectSecretsUpdatedPrefix = "CIProjectSecrets_";

    public const string CIProjectBuildJobsUpdatedPrefix = "CIProjectBuildJobs_";
    public const string CIProjectsBuildsJobUpdatedPrefix = "CIProjectBuildJob_";

    public const string CIProjectsBuildsJobRealtimeOutputPrefix = "CIProjectBuildJobRTOutput_";
    public const string CIProjectBuildJobSectionsUpdatedPrefix = "CIProjectBuildJobSections_";

    // Not used as the the sections list updated notification already contains all the data we want to send
    // (the output doesn't make sense to re-send fully on each subsequent update)
    // public const string CIProjectsBuildsJobsSectionUpdatedPrefix = "CIProjectBuildJobSection_";

    // Other groups
    public const string PatronListUpdated = "Patrons";
    public const string AccessKeyListUpdated = "AccessKeys";

    public const string DevBuildsListUpdated = "DevBuilds";
    public const string DevBuildUpdatedPrefix = "SDevBuild_";

    public const string StorageItemUpdatedPrefix = "StorageItem_";
    public const string FolderContentsUpdatedPublicPrefix = "PubFolder_";
    public const string FolderContentsUpdatedRestrictedUserPrefix = "RUserFolder_";
    public const string FolderContentsUpdatedUserPrefix = "UserFolder_";
    public const string FolderContentsUpdatedDeveloperPrefix = "DeveloperFolder_";
    public const string FolderContentsUpdatedOwnerPrefix = "OwnerFolder_";

    public const string ControlledServerListUpdated = "CServers";
    public const string ExternalServerListUpdated = "EServers";

    public const string UserLauncherLinksUpdatedPrefix = "UserLaunchers_";

    public const string MeetingUpdatedPrefix = "SMeeting_";
    public const string MeetingListUpdatedPublic = "Meeting";
    public const string MeetingListUpdatedUser = "Meeting_User";
    public const string MeetingListUpdatedDeveloper = "Meeting_Developer";
    public const string MeetingListUpdatedAssociation = "Meeting_Association";
    public const string MeetingListUpdatedBoardMember = "Meeting_Board";

    public const string MeetingPollListUpdatedPrefix = "MeetingPoll_";

    public const string GithubAutoCommentListUpdated = "GHAutoComment";
    public const string SentBulkEmailListUpdated = "BulkMails";

    public const string CrashReportUpdatedPrefix = "SCReport_";
    public const string CrashReportListUpdatedPublic = "CReport";
    public const string CrashReportListUpdatedPrivate = "CReport_Developer";

    public const string SymbolListUpdated = "Symbol";

    public const string BackupListUpdated = "Backup";

    public const string AssociationMemberListUpdated = "AsMember";

    public const string RepoForReleaseStatsListUpdated = "RepoReleaseStats";

    public const string FeedListUpdated = "Feeds";
    public const string FeedUpdatedPrefix = "SFeed_";
    public const string FeedDiscordWebhookListUpdatedPrefix = "SFeedDisWeb_";
    public const string CombinedFeedListUpdated = "CombinedFeeds";
    public const string CombinedFeedUpdatedPrefix = "SCombinedFeed_";

    // Launcher info modification models
    public const string LauncherDownloadMirrorListUpdated = "LaunchMirrors";
    public const string LauncherDownloadMirrorUpdatedPrefix = "SLaunchMirror_";

    public const string LauncherLauncherVersionListUpdated = "LaunchVersions";
    public const string LauncherLauncherVersionUpdatedPrefix = "SLaunchVersion_";
    public const string LauncherLauncherVersionUpdateChannelListUpdatedPrefix = "SLaunchVersionUpdateChannels_";
    public const string LauncherLauncherVersionUpdateChannelUpdatedPrefix = "SLaunchVersionUpdateChannel_";

    public const string LauncherLauncherVersionUpdateChannelDownloadsListUpdatedPrefix =
        "SLaunchVersionUpdateChannelDownloads_";

    // public const string LauncherLauncherVersionUpdateChannelDownloadUpdatedPrefix =
    //     "SLaunchVersionUpdateChannelDownloads_";

    public const string LauncherThriveVersionListUpdated = "LaunchThrives";
    public const string LauncherThriveVersionUpdatedPrefix = "SLaunchThrive_";
    public const string LauncherThriveVersionPlatformListUpdatedPrefix = "SLaunchThrivePlatforms_";
    public const string LauncherThriveVersionPlatformUpdatedPrefix = "SLaunchThrivePlatform_";
    public const string LauncherThriveVersionPlatformDownloadsListUpdatedPrefix = "SLaunchThrivePlatformDownloads_";

    // public const string LauncherThriveVersionPlatformDownloadUpdatedPrefix = "SLaunchThrivePlatformDownload_";

    public const string ExecutedMaintenanceOperationListUpdated = "MaintenanceOps";

    /// <summary>
    ///   The client doesn't know their session ID so this is used as-is on the client but with a suffix on the
    ///   server side
    /// </summary>
    public const string InProgressCLASignatureUpdated = "InProgressCLA_";

    /// <summary>
    ///   This is always listened for a valid connected session, this is used to send very important messages
    ///   like logout requests.
    /// </summary>
    public const string SessionImportantMessage = "USM_";
}
