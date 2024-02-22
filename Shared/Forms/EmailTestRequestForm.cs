namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using SharedBase.ModelVerifiers;

public class EmailTestRequestForm
{
    [Required]
    [Email]
    public string Recipient { get; set; } = string.Empty;
}
