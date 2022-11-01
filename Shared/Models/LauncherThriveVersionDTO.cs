namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

/// <summary>
///   DevCenter version of <see cref="ThriveVersionLauncherInfo"/>
/// </summary>
public class LauncherThriveVersionDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(30, MinimumLength = 3)]
    public string ReleaseNumber { get; set; } = string.Empty;

    public bool Stable { get; set; } = true;

    public bool Enabled { get; set; }

    public bool SupportsFailedStartupDetection { get; set; } = true;
}
