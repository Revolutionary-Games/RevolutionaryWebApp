namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

public class ControlledServer : BaseServer, IUpdateNotifications
{
    [AllowSortingBy]
    public double TotalRuntime { get; set; } = 0.0;

    // TODO: hook these two up so that a maintenance job can recreate outdated servers
    public string? CreatedWithImage { get; set; }
    public string? AWSInstanceType { get; set; }
    public long CreatedVolumeSize { get; set; }

    public string? InstanceId { get; set; }

    [Timestamp]
    public uint Version { get; set; }

    [NotMapped]
    public override bool IsExternal => false;

    public void SetProvisioningStatus(string instanceId)
    {
        MarkAsProvisioningStarted();

        InstanceId = instanceId;
    }

    public ControlledServerDTO GetDTO()
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
            TotalRuntime = TotalRuntime,
            ProvisionedFully = ProvisionedFully,
            InstanceId = InstanceId,
            WantsMaintenance = WantsMaintenance,
            LastMaintenance = LastMaintenance,
            UsedDiskSpace = UsedDiskSpace,
            CleanUpQueued = CleanUpQueued,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new ControlledServersUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.ControlledServerListUpdated);
    }
}
