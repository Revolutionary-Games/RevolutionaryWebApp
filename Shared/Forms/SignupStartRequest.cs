namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using SharedBase.Utilities;

public class SignupStartRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string CSRF { get; set; } = string.Empty;
}

public class PendingSignupInfoDTO
{
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;
}

public class SignupCompleteRequest
{
    [Required]
    [MaxLength(500)]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(64)]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? DisplayName { get; set; }

    [Required]
    [MaxLength(500)]
    public string CSRF { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ReturnUrl { get; set; }
}
