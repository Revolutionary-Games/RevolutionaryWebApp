namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(HashedLinkCode), IsUnique = true)]
[Index(nameof(UserId))]
public class LauncherLink : UpdateableModel, IContainsHashedLookUps, IUpdateNotifications
{
    [Required]
    [HashedLookUp]
    public string LinkCode { get; set; } = string.Empty;

    public string? HashedLinkCode { get; set; }

    [Required]
    [AllowSortingBy]
    // TODO: switch this to IPAddress type
    public string? LastIp { get; set; }

    [AllowSortingBy]
    public DateTime? LastConnection { get; set; }

    [AllowSortingBy]
    public int TotalApiCalls { get; set; } = 0;

    public long UserId { get; set; }
    public User? User { get; set; }

    public LauncherLinkDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            LastIp = LastIp,
            LastConnection = LastConnection,
            TotalApiCalls = TotalApiCalls,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(
            new LauncherLinkListUpdated { Type = entityState.ToChangeType(), Item = GetDTO() },
            NotificationGroups.UserLauncherLinksUpdatedPrefix + UserId);
    }
}