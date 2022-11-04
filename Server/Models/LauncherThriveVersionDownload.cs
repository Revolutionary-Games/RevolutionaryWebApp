namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using SharedBase.Models;
using Utilities;

/// <summary>
///   A single download related to a mirror for <see cref="LauncherThriveVersionPlatform"/>
/// </summary>
public class LauncherThriveVersionDownload : IUpdateNotifications
{
    public LauncherThriveVersionDownload(long versionId, PackagePlatform platform, long mirrorId, Uri downloadUrl)
    {
        VersionId = versionId;
        Platform = platform;
        MirrorId = mirrorId;
        DownloadUrl = downloadUrl;
    }

    [AllowSortingBy]
    public long VersionId { get; }

    [AllowSortingBy]
    public PackagePlatform Platform { get; }

    [AllowSortingBy]
    public long MirrorId { get; }

    public Uri DownloadUrl { get; set; }

    public LauncherThriveVersion Version { get; set; } = null!;

    public LauncherThriveVersionPlatform PartOfPlatform { get; set; } = null!;

    public LauncherDownloadMirror Mirror { get; set; } = null!;

    public LauncherThriveVersionDownloadDTO GetDTO()
    {
        return new()
        {
            VersionId = VersionId,
            Platform = Platform,
            MirrorId = MirrorId,
            DownloadUrl = DownloadUrl.ToString(),
            MirrorName = Mirror.InternalName,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new LauncherThriveVersionDownloadListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, $"{NotificationGroups.LauncherThriveVersionPlatformDownloadsListUpdatedPrefix}{VersionId}_{(int)Platform}");

        // yield return new Tuple<SerializedNotification, string>(new LauncherThriveVersionDownloadUpdated
        // {
        //     Item = GetDTO(),
        // }, $"{NotificationGroups.LauncherThriveVersionPlatformDownloadUpdatedPrefix}
        // {VersionId}_{(int)Platform}_{MirrorId}");
    }
}
