namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;

/// <summary>
///   DevCenter version of <see cref="LauncherVersionInfo"/>
/// </summary>
public class LauncherLauncherVersionDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(30, MinimumLength = 3)]
    [NoWhitespace]
    public string Version { get; set; } = string.Empty;

    public bool Latest { get; set; }

    public LauncherLauncherVersionDTO Clone()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Version = Version,
            Latest = Latest,
        };
    }
}
