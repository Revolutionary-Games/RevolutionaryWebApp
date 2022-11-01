namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;

public class LauncherVersionDownloadDTO : IIdentifiable
{
    public long VersionId { get; set; }
    public LauncherAutoUpdateChannel Channel { get; set; }
    public long MirrorId { get; set; }

    [Required]
    [MaxLength(300)]
    public Uri? DownloadUrl { get; set; }

    [JsonIgnore]
    public long Id => (VersionId << 24) | (MirrorId << 8) | (byte)Channel;
}
