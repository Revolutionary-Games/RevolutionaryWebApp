namespace ThriveDevCenter.Shared.Forms;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class DebugSymbolOfferRequest
{
    [Required]
    [MaxLength(AppInfo.MaxDebugSymbolOfferBatch)]
    [MinLength(1)]
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<string> SymbolPaths { get; set; } = new();
}