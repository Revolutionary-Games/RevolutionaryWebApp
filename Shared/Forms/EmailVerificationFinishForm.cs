namespace ThriveDevCenter.Shared.Forms;

using System.ComponentModel.DataAnnotations;

public class EmailVerificationFinishForm
{
    [Required]
    [MaxLength(AppInfo.MaximumTokenLength)]
    public string Token { get; set; } = string.Empty;
}