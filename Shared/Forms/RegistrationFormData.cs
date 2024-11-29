namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication;
using SharedBase.ModelVerifiers;

public class RegistrationFormData
{
    [Required]
    [Email]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(CommunicationConstants.MAX_USERNAME_LENGTH,
        MinimumLength = CommunicationConstants.MIN_USERNAME_LENGTH)]
    [NoTrailingOrPrecedingSpace]
    public string Name { get; set; } = string.Empty;

    // TODO: add optional display name
    // TODO: add optional favourite species field (which should then get passed to the forums when logging in there

    [Required]
    [StringLength(AppInfo.MaxPasswordLength, MinimumLength = AppInfo.MinPasswordLength)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [MaxLength(CommunicationConstants.MAXIMUM_TOKEN_LENGTH)]
    public string CSRF { get; set; } = string.Empty;

    [MaxLength(300)]
    [NoTrailingOrPrecedingSpace]
    public string? RegistrationCode { get; set; }
}
