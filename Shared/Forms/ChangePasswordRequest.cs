namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;

public class ChangePasswordRequest
{
    [Required]
    [MaxLength(AppInfo.MaxPasswordLength)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(AppInfo.MaxPasswordLength, MinimumLength = AppInfo.MinPasswordLength)]
    public string NewPassword { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? TwoFactorCode { get; set; }
}
