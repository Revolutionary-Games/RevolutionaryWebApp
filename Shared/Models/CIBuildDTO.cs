namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

public class CIBuildDTO : IIdentifiable
{
    public long CiProjectId { get; set; }
    public long CiBuildId { get; set; }

    [Required]
    public string CommitHash { get; set; } = string.Empty;

    [Required]
    public string RemoteRef { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public BuildStatus Status { get; set; }

    [Required]
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    ///   Used for notifications to detect which model was updated, that's why this shouldn't be super bad that
    ///   we generate a fake ID like this
    /// </summary>
    [JsonIgnore]
    public long Id => (CiBuildId << 12) + CiProjectId;

    [JsonIgnore]
    public string NotificationsId => CiProjectId + "_" + CiBuildId;
}