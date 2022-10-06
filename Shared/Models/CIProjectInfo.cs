namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class CIProjectInfo : ClientSideModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public bool Public { get; set; }
}
