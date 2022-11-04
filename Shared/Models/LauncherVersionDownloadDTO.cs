namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;
using SharedBase.Utilities;

public class LauncherVersionDownloadDTO : IIdentifiable
{
    public long VersionId { get; set; }
    public LauncherAutoUpdateChannel Channel { get; set; }
    public long MirrorId { get; set; } = -1;

    [Required]
    [MaxLength(GlobalConstants.DEFAULT_MAX_LENGTH_FOR_TO_STRING_ATTRIBUTE)]
    [IsUri]
    public string DownloadUrl { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? MirrorName { get; set; }

    [JsonIgnore]
    public long Id => (VersionId << 24) | (MirrorId << 8) | (byte)Channel;
}
