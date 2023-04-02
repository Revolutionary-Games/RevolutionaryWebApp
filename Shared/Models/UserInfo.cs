namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class UserInfo : ClientSideTimedModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool Local { get; set; }
    public string? SsoSource { get; set; }

    public bool Suspended { get; set; }
}
