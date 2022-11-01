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
///   Serverside model of <see cref="ThriveVersionLauncherInfo"/>
/// </summary>
[Index(nameof(ReleaseNumber), IsUnique = true)]
public class LauncherThriveVersion : UpdateableModel, IUpdateNotifications
{
    public LauncherThriveVersion(string releaseNumber)
    {
        ReleaseNumber = releaseNumber;
    }

    [UpdateFromClientRequest]
    [AllowSortingBy]
    public string ReleaseNumber { get; }

    [UpdateFromClientRequest]
    [AllowSortingBy]
    public bool Stable { get; set; }

    /// <summary>
    ///   When true this is included in the launcher info when the launcher asks for it. Defaults to false to allow
    ///   setting up this whole object before turning this on.
    /// </summary>
    [UpdateFromClientRequest]
    [AllowSortingBy]
    public bool Enabled { get; set; }

    [UpdateFromClientRequest]
    [AllowSortingBy]
    public bool SupportsFailedStartupDetection { get; set; }

    public ICollection<LauncherThriveVersionPlatform> Platforms { get; set; } =
        new HashSet<LauncherThriveVersionPlatform>();

    public LauncherThriveVersionDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ReleaseNumber = ReleaseNumber,
            Stable = Stable,
            Enabled = Enabled,
            SupportsFailedStartupDetection = SupportsFailedStartupDetection,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new LauncherThriveVersionListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.LauncherThriveVersionListUpdated);

        yield return new Tuple<SerializedNotification, string>(new LauncherThriveVersionUpdated
        {
            Item = GetDTO(),
        }, NotificationGroups.LauncherThriveVersionUpdatedPrefix + Id);
    }
}
