namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using Utilities;

/// <summary>
///   Info about a remote runner that runs CI jobs.
/// </summary>
public class RemoteRunner : UpdateableModel
{
    public RemoteRunner(string name)
    {
        Name = name;
    }

    [MaxLength(100)]
    public string Name { get; set; }

    /// <summary>
    ///   Runners with lower priority are more likely to get jobs first
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///   Comma-separated list of tags for this runner. Can be used to filter jobs that it is allowed to take.
    /// </summary>
    [MaxLength(256)]
    public string Tags { get; set; } = string.Empty;

    /// <summary>
    ///   Optional description of the runner to set extra notes
    /// </summary>
    [MaxLength(500)]
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

    // TODO: reserved Job field for when actually running things

    // Some general stats
    public int TotalJobsTaken { get; set; }

    /// <summary>
    ///   xmin-based concurrent edit protection
    /// </summary>
    [Timestamp]
    public uint Version { get; set; }
}
