namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using SharedBase.ModelVerifiers;

public class EmailVerificationRequestForm
{
    [Required]
    [Email]
    public string Email { get; set; } = string.Empty;
}
