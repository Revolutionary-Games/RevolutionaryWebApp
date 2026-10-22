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

/// <summary>
///   This is not actually sent as the list always shows all, so only listen for <see cref="SiteLayoutListUpdated"/>
/// </summary>
public class SiteLayoutPartUpdated : ModelUpdated<SiteLayoutPartDTO>
{
}

/// <summary>
///   Signals that dictionary properties have changed.
/// </summary>
public sealed class TranslationDictionaryUpdated : SerializedNotification
{
    public long DictionaryId { get; init; }
}

/// <summary>
///   Signals that the terms list in a dictionary has changed.
/// </summary>
public sealed class TranslationDictionaryTermsUpdated : SerializedNotification
{
    public long DictionaryId { get; init; }
}

/// <summary>
///   Signals that one dictionary row has changed.
/// </summary>
public sealed class TranslationDictionaryTermUpdated : SerializedNotification
{
    public long DictionaryId { get; init; }
    public string Word { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
}

/// <summary>
///   Signals that the available dictionary language list has changed.
/// </summary>
public sealed class TranslationDictionaryLanguagesUpdated : SerializedNotification
{
    public long DictionaryId { get; init; }
}

/// <summary>
///   Signals that a project's properties have changed.
/// </summary>
public sealed class TranslationProjectUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
}

/// <summary>
///   Signals that a project's configured languages have changed.
/// </summary>
public sealed class TranslationProjectLanguagesUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
}

/// <summary>
///   Signals that a project's translation key list or aggregate counts changed.
/// </summary>
public sealed class TranslationProjectTranslationsUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
}

/// <summary>
///   Signals that one translation key's source data changed.
/// </summary>
public sealed class TranslationUnitUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
    public string Key { get; init; } = string.Empty;
}

/// <summary>
///   Signals that one translation value changed.
/// </summary>
public sealed class TranslationValueUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
}

/// <summary>
///   Signals that translation history for one key and language changed.
/// </summary>
public sealed class TranslationHistoryUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
}

/// <summary>
///   Signals that comments for one key and language changed.
/// </summary>
public sealed class TranslationCommentsUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
}

/// <summary>
///   Signals that suggestions or their votes for one key and language changed.
/// </summary>
public sealed class TranslationSuggestionsUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
}

/// <summary>
///   Signals that incoming translation conflicts changed for a project.
/// </summary>
public sealed class TranslationProjectConflictsUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
}

/// <summary>
///   Signals that synchronization status changed for a project.
/// </summary>
public sealed class TranslationProjectSyncStatusUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
}

/// <summary>
///   Signals that the configured project list changed.
/// </summary>
public sealed class TranslationProjectListUpdated : SerializedNotification
{
}

/// <summary>
///   Signals that the configured dictionary list changed.
/// </summary>
public sealed class TranslationDictionaryListUpdated : SerializedNotification
{
}

/// <summary>
///   Signals that the pending dictionary request list changed.
/// </summary>
public sealed class TranslationDictionaryRequestsUpdated : SerializedNotification
{
}
