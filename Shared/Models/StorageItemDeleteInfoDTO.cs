namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class StorageItemDeleteInfoDTO
{
    public long StorageItemId { get; set; }
    public DateTime DeletedAt { get; set; }

    [Required]
    public string OriginalPath { get; set; } = string.Empty;

    public long? DeletedById { get; set; }
}
