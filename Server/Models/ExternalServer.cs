namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(PublicAddress), IsUnique = true)]
public class ExternalServer : BaseServer, IUpdateNotifications
{
    [Required]
    public string SSHKeyFileName { get; set; } = string.Empty;

    public int Priority { get; set; }

    [NotMapped]
    public override bool IsExternal => true;

    public ExternalServerDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            Status = Status,
            StatusLastChecked = StatusLastChecked,
            ReservationType = ReservationType,
            ReservedFor = ReservedFor?.ToString() ?? "unset",
            PublicAddress = PublicAddress,
            RunningSince = RunningSince,
            ProvisionedFully = ProvisionedFully,
            WantsMaintenance = WantsMaintenance,
            LastMaintenance = LastMaintenance,
            UsedDiskSpace = UsedDiskSpace,
            CleanUpQueued = CleanUpQueued,
            SSHKeyFileName = SSHKeyFileName,
            Priority = Priority,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new ExternalServersUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.ExternalServerListUpdated);
    }
}