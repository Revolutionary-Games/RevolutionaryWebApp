namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;

public class LauncherVersionAutoUpdateChannelDTO : IIdentifiable
{
    public long VersionId { get; set; }
    public LauncherAutoUpdateChannel Channel { get; set; }

    [Required]
    [MaxLength(256)]
    public string FileSha3 { get; set; } = string.Empty;

    [JsonIgnore]
    public long Id => (VersionId << 8) | (byte)Channel;
}
