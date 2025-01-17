namespace RevolutionaryWebApp.Shared.Models.Pages;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class SiteLayoutPartDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(255, MinimumLength = 3)]
    public string LinkTarget { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string AltText { get; set; } = string.Empty;

    public SiteLayoutPartType PartType { get; set; }

    [MaxLength(128)]
    public string? ImageId { get; set; }

    public int Order { get; set; }

    public bool Enabled { get; set; } = true;
}
