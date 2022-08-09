namespace ThriveDevCenter.Shared.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class DebugSymbolOfferResponse
{
    [Required]
    [MaxLength(AppInfo.MaxDebugSymbolOfferBatch)]
    public List<string> Upload { get; set; } = new();
}