namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class UserToken
{
    [Required]
    public string CSRF { get; set; } = string.Empty;

    public UserDTO? User { get; set; }
}
