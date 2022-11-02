namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Serverside version of <see cref="LauncherVersionInfo"/>
/// </summary>
public class LauncherLauncherVersion : UpdateableModel, IUpdateNotifications
{
    public LauncherLauncherVersion(string version)
    {
        Version = version;
    }

    public string Version { get; private set; }

    public bool Latest { get; set; }

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
