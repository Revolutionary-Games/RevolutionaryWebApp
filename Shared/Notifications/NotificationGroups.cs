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

        public const string CIProjectListUpdated = "CIProjects";
        public const string PrivateCIProjectUpdated = "CIProject_Developer";
        public const string CIProjectUpdatedPrefix = "SCIProject_";

        public const string PatronListUpdated = "Patrons";
        public const string AccessKeyListUpdated = "AccessKeys";

        public const string DevBuildsListUpdated = "DevBuilds";
        public const string DevBuildUpdatedPrefix = "SDevBuild_";

        public const string StorageItemUpdatedPrefix = "StorageItem_";
        public const string FolderContentsUpdatedPublicPrefix = "PubFolder_";
        public const string FolderContentsUpdatedUserPrefix = "UserFolder_";
        public const string FolderContentsUpdatedDeveloperPrefix = "DeveloperFolder_";
        public const string FolderContentsUpdatedOwnerPrefix = "OwnerFolder_";

        public const string UserLauncherLinksUpdatedPrefix = "UserLaunchers_";
    }
}
