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
    [NoWhitespace]
    public string FileSha3 { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 3)]
    [MustContain(".")]
    public string LocalFileName { get; set; } = string.Empty;

    /// <summary>
    ///   The version this platform is for. Only returned from the server for the single platform get endpoint,
    ///   null for the list endpoint, and should be null for all client requests. Due to that last point this is not
    ///   cloned.
    /// </summary>
    public LauncherThriveVersionDTO? RelatedVersion { get; set; }

    [JsonIgnore]
    public long Id => (VersionId << 8) | (byte)Platform;

    /// <summary>
    ///   Clones this object but doesn't clone <see cref="RelatedVersion"/>
    /// </summary>
    /// <returns>The cloned object</returns>
    public LauncherThriveVersionPlatformDTO Clone()
    {
        return new()
        {
            VersionId = VersionId,
            Platform = Platform,
            FileSha3 = FileSha3,
            LocalFileName = LocalFileName,
        };
    }
}
