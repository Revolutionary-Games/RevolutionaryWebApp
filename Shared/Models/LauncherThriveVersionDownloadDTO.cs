namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using SharedBase.Models;

public class LauncherThriveVersionDownloadDTO : IIdentifiable
{
    public long VersionId { get; set; }
    public PackagePlatform Platform { get; set; }
    public long MirrorId { get; set; }

    [Required]
    [MaxLength(300)]
    public Uri? DownloadUrl { get; set; }

    [JsonIgnore]
    public long Id => (VersionId << 24) | (MirrorId << 8) | (byte)Platform;
}
