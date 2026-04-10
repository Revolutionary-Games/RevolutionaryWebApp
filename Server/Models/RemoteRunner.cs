namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Info about a remote runner that runs CI jobs.
/// </summary>
public class RemoteRunner : UpdateableModel, IDTOCreator<RemoteRunnerDTO>, IUpdateNotifications
{
    public RemoteRunner(string name)
    {
        Name = name;
    }

    [MaxLength(100)]
    [UpdateFromClientRequest]
    [AllowSortingBy]
    public string Name { get; set; }

    /// <summary>
    ///   Runners with lower priority are more likely to get jobs first
    /// </summary>
    [UpdateFromClientRequest]
    [AllowSortingBy]
    public int Priority { get; set; }

    /// <summary>
    ///   Semicolon-separated list of tags for this runner. Can be used to filter jobs that it is allowed to take.
    /// </summary>
    [MaxLength(256)]
    [UpdateFromClientRequest]
    [AllowSortingBy]
    public string Tags { get; set; } = string.Empty;

    /// <summary>
    ///   Optional description of the runner to set extra notes
    /// </summary>
    [MaxLength(500)]
    [UpdateFromClientRequest]
    public string? Description { get; set; }

    [HashedLookUp]
    public Guid AccessId { get; set; }

    [MaxLength(256)]
    public string HashedAccessId { get; set; } = string.Empty;

    public Guid SecretKey { get; set; }

    public DateTime? LastHeartbeat { get; set; }

    public IPAddress? LastIpAddress { get; set; }

    /// <summary>
    ///   Used to make sure each runner is allowed a single connection
    /// </summary>
    public int CurrentConnectionId { get; set; }

    [MaxLength(2048)]
    public string? LastTriggeredError { get; set; }

    /// <summary>
    ///   If set to true, then new jobs may not be started by this runner
    /// </summary>
    [AllowSortingBy]
    public bool DisallowJobs { get; set; }

    /// <summary>
    ///   The one job this runner is working on. This is not a list as a runner can only have one job at a time, which
    ///   it needs to complete before it can take another.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     Due to the complex key, for now we rely on a one-sided relationship where each job can have exactly one
    ///     runner.
    ///   </para>
    /// </remarks>
    public CiJob? ReservedJob { get; set; }

    public bool QueuedCleanUp { get; set; }

    // Some general stats
    [AllowSortingBy]
    public int TotalJobsTaken { get; set; }

    /// <summary>
    ///   xmin-based concurrent edit protection
    /// </summary>
    [Timestamp]
    public uint Version { get; set; }

    public RemoteRunnerDTO GetDTO()
    {
        return new RemoteRunnerDTO(Name)
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Priority = Priority,
            Tags = Tags,
            Description = Description,
            LastHeartbeat = LastHeartbeat,
            LastIpAddress = LastIpAddress,
            CurrentConnectionId = CurrentConnectionId,
            LastTriggeredError = LastTriggeredError,
            ReservedJobId = ReservedJob != null ? $"{ReservedJob.CiBuildId}-{ReservedJob.CiJobId}" : null,
            TotalJobsTaken = TotalJobsTaken,
            DisallowJobs = DisallowJobs,
            QueuedCleanUp = QueuedCleanUp,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new RemoteRunnersUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.RemoteRunnersUpdated);
    }
}
