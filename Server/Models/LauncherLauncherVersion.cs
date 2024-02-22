namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using DevCenterCommunication.Models;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Serverside version of <see cref="LauncherVersionInfo"/>
/// </summary>
[Index(nameof(Version), IsUnique = true)]
public class LauncherLauncherVersion : UpdateableModel, IUpdateNotifications
{
    public LauncherLauncherVersion(string version)
    {
        Version = version;
    }

    [AllowSortingBy]
    public string Version { get; private set; }

    [AllowSortingBy]
    public bool Latest { get; set; }

    public DateTime? SetLatestAt { get; set; }

    public ICollection<LauncherVersionAutoUpdateChannel> AutoUpdateDownloads { get; set; } =
        new HashSet<LauncherVersionAutoUpdateChannel>();

    public LauncherLauncherVersionDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Version = Version,
            Latest = Latest,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new LauncherLauncherVersionListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.LauncherLauncherVersionListUpdated);

        yield return new Tuple<SerializedNotification, string>(new LauncherLauncherVersionUpdated
        {
            Item = GetDTO(),
        }, NotificationGroups.LauncherLauncherVersionUpdatedPrefix + Id);
    }
}
