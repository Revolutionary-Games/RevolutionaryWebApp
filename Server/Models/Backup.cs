namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(Name), IsUnique = true)]
public class Backup : UpdateableModel, IUpdateNotifications
{
    public Backup(string name, long size)
    {
        Name = name;
        Size = size;
    }

    [Required]
    public string Name { get; set; }

    public long Size { get; set; }

    public bool Uploaded { get; set; }

    public static string CreateBackupName(bool xz, DateTime? time = null)
    {
        time ??= DateTime.UtcNow;

        // TODO: redo backup handling
        return "ThriveDevCenter-Backup_" + time.Value.ToString("O") + (xz ? ".tar.xz" : ".tar.gz");
    }

    public BackupDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            Size = Size,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new BackupListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.BackupListUpdated);
    }
}
