namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication;

public class EmailVerificationFinishForm
{
    [Required]
    [MaxLength(CommunicationConstants.MAXIMUM_TOKEN_LENGTH)]
    public string Token { get; set; } = string.Empty;
}
