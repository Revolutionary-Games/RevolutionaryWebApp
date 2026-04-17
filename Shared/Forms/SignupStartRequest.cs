namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication;
using SharedBase.ModelVerifiers;
using SharedBase.Utilities;

public class SignupStartRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(CommunicationConstants.MAXIMUM_TOKEN_LENGTH)]
    public string CSRF { get; set; } = string.Empty;
}

public class PendingSignupInfoDTO
{
    [MaxLength(GlobalConstants.MaxEmailLength)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class SignupCompleteRequest
{
    [Required]
    [MaxLength(500)]
    public string Token { get; set; } = string.Empty;

    [Required]
    [StringLength(CommunicationConstants.MAX_USERNAME_LENGTH,
        MinimumLength = CommunicationConstants.MIN_USERNAME_LENGTH)]
    [NoTrailingOrPrecedingSpace]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(128)]
    [NoTrailingOrPrecedingSpace]
    public string? DisplayName { get; set; }

    [Required]
    [MaxLength(CommunicationConstants.MAXIMUM_TOKEN_LENGTH)]
    public string CSRF { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ReturnUrl { get; set; }
}
