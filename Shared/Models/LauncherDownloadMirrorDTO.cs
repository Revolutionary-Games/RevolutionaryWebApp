namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;

/// <summary>
///   DevCenter modifiable version of <see cref="DownloadMirrorInfo"/>. This is a separate class because DevCenter
///   notification models can't have parametrized constructors, and this contains a bit more info the launcher doesn't
///   actually need to receive.
/// </summary>
public class LauncherDownloadMirrorDTO : ClientSideTimedModel
{
    [Required]
    public string InternalName { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    [IsUri]
    public string InfoLink { get; set; } = string.Empty;

    [Required]
    [StringLength(60, MinimumLength = 2)]
    public string ReadableName { get; set; } = string.Empty;

    [MaxLength(300)]
    [IsUri]
    public string? BannerImageUrl { get; set; }

    [MaxLength(250)]
    public string? ExtraDescription { get; set; }
}
