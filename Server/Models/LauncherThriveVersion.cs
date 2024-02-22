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
///   Serverside model of <see cref="ThriveVersionLauncherInfo"/>
/// </summary>
[Index(nameof(ReleaseNumber), IsUnique = true)]
public class LauncherThriveVersion : UpdateableModel, IUpdateNotifications
{
    public LauncherThriveVersion(string releaseNumber)
    {
        ReleaseNumber = releaseNumber;
    }

    [AllowSortingBy]
    public string ReleaseNumber { get; }

    [AllowSortingBy]
    public bool Stable { get; set; }

    /// <summary>
    ///   When true this is included in the launcher info when the launcher asks for it. Defaults to false to allow
    ///   setting up this whole object before turning this on.
    /// </summary>
    [AllowSortingBy]
    public bool Enabled { get; set; }

    /// <summary>
    ///   True when this is the latest version of the type <see cref="Stable"/> variable indicates. Only one can be
    ///   latest of each type.
    /// </summary>
    [AllowSortingBy]
    public bool Latest { get; set; }

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
            Latest = Latest,
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
