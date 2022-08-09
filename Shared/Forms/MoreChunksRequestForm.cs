namespace ThriveDevCenter.Shared.Forms;

using System.ComponentModel.DataAnnotations;

public class MoreChunksRequestForm
{
    [Required]
    [MaxLength(AppInfo.MaximumTokenLength)]
    public string Token { get; set; } = string.Empty;
}
