namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using DevCenterCommunication.Models;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   A single download related to a mirror for <see cref="LauncherVersionAutoUpdateChannel"/>
/// </summary>
public class LauncherVersionDownload : IUpdateNotifications
{
    public LauncherVersionDownload(long versionId, LauncherAutoUpdateChannel channel, long mirrorId, Uri downloadUrl)
    {
        VersionId = versionId;
        Channel = channel;
        MirrorId = mirrorId;
        DownloadUrl = downloadUrl;
    }

    [AllowSortingBy]
    public long VersionId { get; }

    [AllowSortingBy]
    public LauncherAutoUpdateChannel Channel { get; }

    [AllowSortingBy]
    public long MirrorId { get; }

    public Uri DownloadUrl { get; set; }

    public LauncherLauncherVersion Version { get; set; } = null!;

    public LauncherVersionAutoUpdateChannel UpdateChannel { get; set; } = null!;

    public LauncherDownloadMirror Mirror { get; set; } = null!;

    public LauncherVersionDownloadDTO GetDTO()
    {
        return new()
        {
            VersionId = VersionId,
            Channel = Channel,
            MirrorId = MirrorId,
            DownloadUrl = DownloadUrl.ToString(),
            MirrorName = Mirror.InternalName,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new LauncherVersionDownloadListUpdated
            {
                Type = entityState.ToChangeType(),
                Item = GetDTO(),
            },
            $"{NotificationGroups.LauncherLauncherVersionUpdateChannelDownloadsListUpdatedPrefix}" +
            $"{VersionId}_{(int)Channel}");

        // yield return new Tuple<SerializedNotification, string>(new LauncherVersionDownloadUpdated
        // {
        //     Item = GetDTO(),
        // }, $"{NotificationGroups.LauncherLauncherVersionUpdateChannelDownloadUpdatedPrefix}
        // {VersionId}_{(int)Channel}_{MirrorId}");
    }
}
