namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;

public class LogoutFormData
{
    [Required]
    public string CSRF { get; set; } = string.Empty;
}
