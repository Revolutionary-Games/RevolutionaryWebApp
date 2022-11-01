namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using DevCenterCommunication.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Single update channel as part of <see cref="LauncherLauncherVersion"/>
/// </summary>
public class LauncherVersionAutoUpdateChannel : IUpdateNotifications
{
    public LauncherVersionAutoUpdateChannel(long versionId, LauncherAutoUpdateChannel channel, string fileSha3)
    {
        VersionId = versionId;
        Channel = channel;
        FileSha3 = fileSha3;
    }

    public long VersionId { get; }

    public LauncherAutoUpdateChannel Channel { get; }

    public string FileSha3 { get; }

    public LauncherLauncherVersion Version { get; set; } = null!;

    public ICollection<LauncherVersionDownload> Mirrors { get; set; } = new HashSet<LauncherVersionDownload>();

    public LauncherVersionAutoUpdateChannelDTO GetDTO()
    {
        return new()
        {
            VersionId = VersionId,
            Channel = Channel,
            FileSha3 = FileSha3,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new LauncherVersionAutoUpdateChannelListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.LauncherLauncherVersionUpdateChannelListUpdatedPrefix + VersionId);

        yield return new Tuple<SerializedNotification, string>(new LauncherVersionAutoUpdateChannelUpdated
        {
            Item = GetDTO(),
        }, $"{NotificationGroups.LauncherLauncherVersionUpdateChannelUpdatedPrefix}{VersionId}_{(int)Channel}");
    }
}
