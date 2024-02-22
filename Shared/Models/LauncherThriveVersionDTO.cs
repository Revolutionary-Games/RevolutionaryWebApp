namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;

/// <summary>
///   DevCenter version of <see cref="ThriveVersionLauncherInfo"/>
/// </summary>
public class LauncherThriveVersionDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(30, MinimumLength = 3)]
    [NoWhitespace]
    public string ReleaseNumber { get; set; } = string.Empty;

    public bool Stable { get; set; } = true;

    public bool Enabled { get; set; }
    public bool Latest { get; set; }
    public bool SupportsFailedStartupDetection { get; set; } = true;

    public LauncherThriveVersionDTO Clone()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ReleaseNumber = ReleaseNumber,
            Stable = Stable,
            Enabled = Enabled,
            Latest = Latest,
            SupportsFailedStartupDetection = SupportsFailedStartupDetection,
        };
    }
}
