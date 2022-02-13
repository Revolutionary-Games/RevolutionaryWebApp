namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared.Models;

[Index(nameof(Name), IsUnique = true)]
public class Backup : UpdateableModel
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

    public static string CreateBackupName(DateTime? time = null)
    {
        time ??= DateTime.UtcNow;

        return "ThriveDevCenter-Backup_" + time.Value.ToString("O") + ".tar.xz";
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
}
