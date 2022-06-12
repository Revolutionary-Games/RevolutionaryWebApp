namespace ThriveDevCenter.Shared.Notifications
{
    using Models;

    /// <summary>
    ///   Notification about a list being updated
    /// </summary>
    public abstract class ListUpdated<T> : SerializedNotification
        where T : class, new()
    {
        public ListItemChangeType Type { get; init; } = ListItemChangeType.ItemUpdated;
        public T Item { get; init; } = new();
    }

    // These separate class types are needed for JSON serialization to work

    public class LFSListUpdated : ListUpdated<LFSProjectInfo>
    {
    }

    public class UserListUpdated : ListUpdated<UserInfo>
    {
    }

    public class ProjectGitFileUpdated : ListUpdated<ProjectGitFileDTO>
    {
    }

    public class LfsObjectListUpdated : ListUpdated<LfsObjectDTO>
    {
    }

    public class PatronListUpdated : ListUpdated<PatronDTO>
    {
    }

    public class AccessKeyListUpdated : ListUpdated<AccessKeyDTO>
    {
    }

    public class LogEntriesUpdated : ListUpdated<LogEntryDTO>
    {
    }

    public class AdminActionsUpdated : ListUpdated<AdminActionDTO>
    {
    }

    public class ActionsUpdated : ListUpdated<ActionLogEntryDTO>
    {
    }

    public class LauncherLinkListUpdated : ListUpdated<LauncherLinkDTO>
    {
    }

    public class DevBuildListUpdated : ListUpdated<DevBuildDTO>
    {
    }

    public class FolderContentsUpdated : ListUpdated<StorageItemInfo>
    {
    }

    public class CIProjectListUpdated : ListUpdated<CIProjectInfo>
    {
    }

    public class ControlledServersUpdated : ListUpdated<ControlledServerDTO>
    {
    }

    public class ExternalServersUpdated : ListUpdated<ExternalServerDTO>
    {
    }

    public class CIProjectBuildsListUpdated : ListUpdated<CIBuildDTO>
    {
    }

    public class CIProjectSecretsUpdated : ListUpdated<CISecretDTO>
    {
    }

    public class CIProjectBuildJobsListUpdated : ListUpdated<CIJobDTO>
    {
    }

    public class CIProjectBuildJobOutputSectionsListUpdated : ListUpdated<CIJobOutputSectionInfo>
    {
    }

    public class StorageItemVersionListUpdated : ListUpdated<StorageItemVersionInfo>
    {
    }

    public class CLAListUpdated : ListUpdated<CLAInfo>
    {
    }

    // TODO: not currently implemented fully
    public class MeetingListUpdated : ListUpdated<MeetingInfo>
    {
    }

    public class MeetingPollListUpdated : ListUpdated<MeetingPollDTO>
    {
    }

    public class GithubAutoCommentListUpdated : ListUpdated<GithubAutoCommentDTO>
    {
    }

    public class SentBulkEmailListUpdated : ListUpdated<SentBulkEmailDTO>
    {
    }

    public class CrashReportListUpdated : ListUpdated<CrashReportInfo>
    {
    }

    public class DebugSymbolListUpdated : ListUpdated<DebugSymbolDTO>
    {
    }

    public class SessionListUpdated : ListUpdated<SessionDTO>
    {
    }

    public class BackupListUpdated : ListUpdated<BackupDTO>
    {
    }

    public class AssociationMemberListUpdated : ListUpdated<AssociationMemberInfo>
    {
    }

    public class RepoForReleaseStatsListUpdated : ListUpdated<RepoForReleaseStatsDTO>
    {
    }

    public class FeedListUpdated : ListUpdated<FeedInfo>
    {
    }

    public class CombinedFeedListUpdated : ListUpdated<CombinedFeedInfo>
    {
    }
}
