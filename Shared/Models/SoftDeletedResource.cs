namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class SoftDeletedResource : ClientSideModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? ResourceKey { get; set; }

    public DateTime UpdatedAt { get; set; }
}
