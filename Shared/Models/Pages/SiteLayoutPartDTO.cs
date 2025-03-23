namespace RevolutionaryWebApp.Shared.Models.Pages;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class SiteLayoutPartDTO : ClientSideTimedModel
{
    [MaxLength(255)]
    public string? LinkTarget { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string AltText { get; set; } = string.Empty;

    public SiteLayoutPartType PartType { get; set; }

    [MaxLength(128)]
    public string? ImageId { get; set; }

    public LayoutPartDisplayMode DisplayMode { get; set; }

    public int Order { get; set; }

    public bool Enabled { get; set; } = true;
}
