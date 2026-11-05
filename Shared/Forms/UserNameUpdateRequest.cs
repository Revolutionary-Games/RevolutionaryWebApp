namespace RevolutionaryWebApp.Shared.Forms;

using System.ComponentModel.DataAnnotations;

public class UserNameUpdateRequest
{
    [StringLength(120)]
    public string? DisplayName { get; set; }
}
