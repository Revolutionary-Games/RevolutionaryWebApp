namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;

public class TwoFactorCodeForm
{
    [Required]
    [StringLength(64)]
    public string Code { get; set; } = string.Empty;
}
