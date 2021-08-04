namespace ThriveDevCenter.Shared.Notifications
{
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
        public const string FolderContentsUpdatedUserPrefix = "UserFolder_";
        public const string FolderContentsUpdatedDeveloperPrefix = "DeveloperFolder_";
        public const string FolderContentsUpdatedOwnerPrefix = "OwnerFolder_";

        public const string ControlledServerListUpdated = "CServers";

        public const string UserLauncherLinksUpdatedPrefix = "UserLaunchers_";

        public const string MeetingUpdatedPrefix = "SMeeting_";
        public const string MeetingListUpdatedPublic = "Meeting";
        public const string MeetingListUpdatedUser = "Meeting_User";
        public const string MeetingListUpdatedDeveloper = "Meeting_Developer";
        public const string MeetingListUpdatedAssociation = "Meeting_Association";
        public const string MeetingListUpdatedBoardMember = "Meeting_Board";

        public const string MeetingPollListUpdatedPrefix = "MeetingPoll_";

        /// <summary>
        ///   The client doesn't know their session ID so this is used as-is on the client but with a suffix on the
        ///   server side
        /// </summary>
        public const string InProgressCLASignatureUpdated = "InProgressCLA_";
    }
}
