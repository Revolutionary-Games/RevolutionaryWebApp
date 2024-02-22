namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;
using SharedBase.Utilities;

/// <summary>
///   DevCenter modifiable version of <see cref="DownloadMirrorInfo"/>. This is a separate class because DevCenter
///   notification models can't have parametrized constructors, and this contains a bit more info the launcher doesn't
///   actually need to receive.
/// </summary>
public class LauncherDownloadMirrorDTO : ClientSideTimedModel
{
    [Required]
    [NoWhitespace]
    public string InternalName { get; set; } = string.Empty;

    [Required]
    [MaxLength(GlobalConstants.DEFAULT_MAX_LENGTH_FOR_TO_STRING_ATTRIBUTE)]
    [IsUri]
    public string InfoLink { get; set; } = string.Empty;

    [Required]
    [StringLength(60, MinimumLength = 2)]
    public string ReadableName { get; set; } = string.Empty;

    [MaxLength(GlobalConstants.DEFAULT_MAX_LENGTH_FOR_TO_STRING_ATTRIBUTE)]
    [IsUri]
    public string? BannerImageUrl { get; set; }

    [MaxLength(250)]
    public string? ExtraDescription { get; set; }

    public LauncherDownloadMirrorDTO Clone()
    {
        return new LauncherDownloadMirrorDTO
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            InternalName = InternalName,
            InfoLink = InfoLink,
            ReadableName = ReadableName,
            BannerImageUrl = BannerImageUrl,
            ExtraDescription = ExtraDescription,
        };
    }
}
