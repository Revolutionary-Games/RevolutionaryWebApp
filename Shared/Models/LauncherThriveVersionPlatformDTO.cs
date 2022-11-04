namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.Models;
using SharedBase.ModelVerifiers;

public class LauncherThriveVersionPlatformDTO : IIdentifiable
{
    public long VersionId { get; set; }

    public PackagePlatform Platform { get; set; }

    [Required]
    [StringLength(256, MinimumLength = 10)]
    public string FileSha3 { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 3)]
    [MustContain(".")]
    public string LocalFileName { get; set; } = string.Empty;

    [JsonIgnore]
    public long Id => (VersionId << 8) | (byte)Platform;
}
