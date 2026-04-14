namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class RemoteRunnerDTO(string name) : ClientSideTimedModel
{
    public RemoteRunnerDTO() : this(string.Empty)
    {
    }

    [MaxLength(100)]
    [Required]
    public string Name { get; set; } = name;

    [Range(AppInfo.MinExternalServerPriority, AppInfo.MaxExternalServerPriority)]
    public int Priority { get; set; }

    [MaxLength(256)]
    public string Tags { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime? LastHeartbeat { get; set; }

    public string? LastIpAddress { get; set; }

    public int CurrentConnectionId { get; set; }

    [MaxLength(2048)]
    public string? LastTriggeredError { get; set; }

    public int TotalJobsTaken { get; set; }

    /// <summary>
    ///   String-formatted ID of a reserved job for this
    /// </summary>
    public string? ReservedJobId { get; set; }

    public bool DisallowJobs { get; set; }
    public bool QueuedCleanUp { get; set; }
}
