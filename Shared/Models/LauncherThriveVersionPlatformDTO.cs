namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.Models;

public class LauncherThriveVersionPlatformDTO : IIdentifiable
{
    public long VersionId { get; set; }

    public PackagePlatform Platform { get; set; }

    [Required]
    [MaxLength(256)]
    public string FileSha3 { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string LocalFileName { get; set; } = string.Empty;

    [JsonIgnore]
    public long Id => (VersionId << 8) | (byte)Platform;
}
