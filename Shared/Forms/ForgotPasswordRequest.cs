namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using SharedBase.Utilities;

public class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    [StringLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(DevCenterCommunication.CommunicationConstants.MAXIMUM_TOKEN_LENGTH)]
    public string CSRF { get; set; } = string.Empty;
}
