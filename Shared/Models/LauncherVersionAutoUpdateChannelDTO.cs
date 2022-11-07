namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;

public class LauncherVersionAutoUpdateChannelDTO : IIdentifiable
{
    public long VersionId { get; set; }
    public LauncherAutoUpdateChannel Channel { get; set; }

    [Required]
    [StringLength(256, MinimumLength = 10)]
    [NoWhitespace]
    public string FileSha3 { get; set; } = string.Empty;

    [JsonIgnore]
    public long Id => (VersionId << 8) | (byte)Channel;

    public LauncherVersionAutoUpdateChannelDTO Clone()
    {
        return new()
        {
            VersionId = VersionId,
            FileSha3 = FileSha3,
        };
    }
}
