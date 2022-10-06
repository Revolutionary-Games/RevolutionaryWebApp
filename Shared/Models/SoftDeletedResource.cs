namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class SoftDeletedResource : ClientSideModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; }
}
