namespace ThriveDevCenter.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication;

public class MoreChunksRequestForm
{
    [Required]
    [MaxLength(CommunicationConstants.MAXIMUM_TOKEN_LENGTH)]
    public string Token { get; set; } = string.Empty;
}
