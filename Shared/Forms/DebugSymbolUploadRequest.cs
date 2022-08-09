namespace ThriveDevCenter.Shared.Forms;

using System.ComponentModel.DataAnnotations;

public class DebugSymbolUploadRequest
{
    [Required]
    [StringLength(120, MinimumLength = 3)]
    public string SymbolPath { get; set; } = string.Empty;

    [Range(1, AppInfo.MaxDebugSymbolSize)]
    public long Size { get; set; }
}