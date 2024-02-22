namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;

public class CIJobOutputSectionDTO : IIdentifiable
{
    public long CiProjectId { get; set; }
    public long CiBuildId { get; set; }
    public long CiJobId { get; set; }
    public long CiJobOutputSectionId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public CIJobSectionStatus Status { get; set; }

    /// <summary>
    ///   This can contain megabytes of data
    /// </summary>
    [Required]
    public string Output { get; set; } = string.Empty;

    public long OutputLength { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    ///   Used for notifications to detect which model was updated, that's why this shouldn't be super bad that
    ///   we generate a fake ID like this
    /// </summary>
    [JsonIgnore]
    public long Id => (CiBuildId << 19) + (CiJobId << 12) + (CiJobOutputSectionId << 7) + CiProjectId;
}
