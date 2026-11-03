namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class APIKeyDTO : APIKeySettings
{
    [MaxLength(100)]
    public string Id { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public string? LastUsedIP { get; set; }
    public long TotalUses { get; set; }
}
