namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using SharedBase.Models;
using Utilities;

/// <summary>
///   Single platform for <see cref="LauncherThriveVersion"/>
/// </summary>
public class LauncherThriveVersionPlatform : IUpdateNotifications
{
    public LauncherThriveVersionPlatform(long versionId, PackagePlatform platform, string fileSha3,
        string localFileName)
    {
        VersionId = versionId;
        Platform = platform;
        FileSha3 = fileSha3;
        LocalFileName = localFileName;
    }

    public long VersionId { get; private set; }

    [AllowSortingBy]
    public PackagePlatform Platform { get; private set; }

    [UpdateFromClientRequest]
    public string FileSha3 { get; set; }

    [AllowSortingBy]
    [UpdateFromClientRequest]
    public string LocalFileName { get; set; }

    public LauncherThriveVersion? Version { get; set; }

    public ICollection<LauncherThriveVersionDownload> Mirrors { get; set; } =
        new HashSet<LauncherThriveVersionDownload>();

    public LauncherThriveVersionPlatformDTO GetDTO()
    {
        return new()
        {
            VersionId = VersionId,
            Platform = Platform,
            FileSha3 = FileSha3,
            LocalFileName = LocalFileName,
            RelatedVersion = Version?.GetDTO(),
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new LauncherThriveVersionPlatformListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.LauncherThriveVersionPlatformListUpdatedPrefix + VersionId);

        yield return new Tuple<SerializedNotification, string>(new LauncherThriveVersionPlatformUpdated
            {
                Item = GetDTO(),
            }, $"{NotificationGroups.LauncherThriveVersionPlatformUpdatedPrefix}{VersionId}_{(int)Platform}");
    }
}
