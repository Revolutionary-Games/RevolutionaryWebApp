namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

/// <summary>
///   DevCenter version of <see cref="LauncherVersionInfo"/>
/// </summary>
public class LauncherLauncherVersionDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(30, MinimumLength = 3)]
    public string Version { get; set; } = string.Empty;

    public bool Latest { get; set; }
}
