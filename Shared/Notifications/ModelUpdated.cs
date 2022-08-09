namespace ThriveDevCenter.Shared.Notifications;

using Models;

/// <summary>
///   Notification about a single model page information being updated
/// </summary>
public abstract class ModelUpdated<T> : SerializedNotification
    where T : class, new()
{
    public T Item { get; init; } = new();
}

// These separate class types are needed for JSON serialization to work

public class UserUpdated : ModelUpdated<UserInfo>
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