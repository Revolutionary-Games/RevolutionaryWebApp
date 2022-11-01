namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

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
    public Uri? InfoLink { get; set; }

    [Required]
    [StringLength(60, MinimumLength = 2)]
    public string ReadableName { get; set; } = string.Empty;

    [MaxLength(300)]
    public Uri? BannerImageUrl { get; set; }

    [MaxLength(250)]
    public string? ExtraDescription { get; set; }
}
