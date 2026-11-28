namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class BackupStatusReport
{
    [Required]
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    [EnumDataType(typeof(BackupOperation))]
    public BackupOperation Operation { get; set; }

    public DateTime FinishedAt { get; set; }

    [Required]
    public int? ExitCode { get; set; }
}
