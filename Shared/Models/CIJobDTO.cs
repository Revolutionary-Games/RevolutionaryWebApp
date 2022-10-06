namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;

public class CIJobDTO : IIdentifiable
{
    public long CiProjectId { get; set; }
    public long CiBuildId { get; set; }

    public long CiJobId { get; set; }

    [Required]
    public string JobName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public CIJobState State { get; set; }

    [Required]
    public string ProjectName { get; set; } = string.Empty;

    public bool Succeeded { get; set; }
    public string? RanOnServer { get; set; }
    public TimeSpan? TimeWaitingForServer { get; set; }

    /// <summary>
    ///   Used for notifications to detect which model was updated, that's why this shouldn't be super bad that
    ///   we generate a fake ID like this
    /// </summary>
    [JsonIgnore]
    public long Id => (CiBuildId << 12) + (CiJobId << 7) + CiProjectId;

    [JsonIgnore]
    public string NotificationsId => CiProjectId + "_" + CiBuildId + "_" + CiJobId;
}
