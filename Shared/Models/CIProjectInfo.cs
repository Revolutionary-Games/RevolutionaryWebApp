namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class CIProjectInfo : ClientSideModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public bool Public { get; set; }
}