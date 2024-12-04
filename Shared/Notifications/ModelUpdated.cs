namespace RevolutionaryWebApp.Shared.Notifications;

using DevCenterCommunication.Models;
using Models;
using Models.Pages;

/// <summary>
///   Notification about a single model page information being updated
/// </summary>
public abstract class ModelUpdated<T> : SerializedNotification
    where T : class, new()
{
    public T Item { get; init; } = new();
}

// These separate class types are needed for JSON serialization to work

public class UserUpdated : ModelUpdated<UserDTO>
{
}

public class LFSProjectUpdated : ModelUpdated<LFSProjectDTO>
{
}

public class DevBuildUpdated : ModelUpdated<DevBuildDTO>
{
}

public class StorageItemUpdated : ModelUpdated<StorageItemDTO>
{
}

public class CIProjectUpdated : ModelUpdated<CIProjectDTO>
{
}

public class CIBuildUpdated : ModelUpdated<CIBuildDTO>
{
}

public class CIJobUpdated : ModelUpdated<CIJobDTO>
{
}

public class CLAUpdated : ModelUpdated<CLADTO>
{
}

public class MeetingUpdated : ModelUpdated<MeetingDTO>
{
}

public class InProgressClaSignatureUpdated : ModelUpdated<InProgressClaSignatureDTO>
{
}

public class CrashReportUpdated : ModelUpdated<CrashReportDTO>
{
}

/// <summary>
///   Dummy as needed as base class parameter, not currently sent, see the AssociationMember class for why
/// </summary>
public class AssociationMemberUpdated : ModelUpdated<AssociationMemberDTO>
{
}

public class FeedUpdated : ModelUpdated<FeedDTO>
{
}

public class CombinedFeedUpdated : ModelUpdated<CombinedFeedDTO>
{
}

public class LauncherDownloadMirrorUpdated : ModelUpdated<LauncherDownloadMirrorDTO>
{
}

public class LauncherLauncherVersionUpdated : ModelUpdated<LauncherLauncherVersionDTO>
{
}

public class LauncherVersionAutoUpdateChannelUpdated : ModelUpdated<LauncherVersionAutoUpdateChannelDTO>
{
}

/// <summary>
///   Not currently used, but there's commented out code in the model to set this up
/// </summary>
public class LauncherVersionDownloadUpdated : ModelUpdated<LauncherVersionDownloadDTO>
{
}

public class LauncherThriveVersionUpdated : ModelUpdated<LauncherThriveVersionDTO>
{
}

public class LauncherThriveVersionPlatformUpdated : ModelUpdated<LauncherThriveVersionPlatformDTO>
{
}

/// <summary>
///   Not currently used
/// </summary>
public class LauncherThriveVersionDownloadUpdated : ModelUpdated<LauncherThriveVersionDownloadDTO>
{
}

public class PrecompiledObjectUpdated : ModelUpdated<PrecompiledObjectDTO>
{
}

public class VersionedPageUpdated : ModelUpdated<VersionedPageDTO>
{
}

public class MediaFolderUpdated : ModelUpdated<MediaFolderDTO>
{
}

public class MediaFileUpdated : ModelUpdated<MediaFileDTO>
{
}

public class SiteLayoutPartUpdated : ModelUpdated<SiteLayoutPartDTO>
{
}
