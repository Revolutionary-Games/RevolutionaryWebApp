namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(FinishedAt), nameof(Id))]
public class BackupRunStatus : ModelWithCreationTime, IUpdateNotifications
{
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    public BackupOperation Operation { get; set; }

    [AllowSortingBy]
    public DateTime FinishedAt { get; set; }

    public int ExitCode { get; set; }

    public BackupRunStatusDTO GetDTO()
    {
        return new BackupRunStatusDTO
        {
            Id = Id,
            Host = Host,
            Operation = Operation,
            FinishedAt = FinishedAt,
            ExitCode = ExitCode,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new BackupRunStatusListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.BackupRunStatusListUpdated);
    }
}
