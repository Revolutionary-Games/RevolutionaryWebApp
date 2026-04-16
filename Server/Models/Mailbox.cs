namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Represents an inbound mailbox configuration stored in the database.
///   A system default mailbox (NotificationsReply) is seeded with Id = 1 and no explicit credentials.
/// </summary>
public class Mailbox : BaseModel
{
    public Mailbox(string name)
    {
        Name = name;
    }

    /// <summary>
    ///   Human-readable identifier for the mailbox (e.g. NotificationsReply).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; }

    /// <summary>
    ///   Optional username for mailbox authentication. Null for the system default mailbox that uses app configuration.
    /// </summary>
    [MaxLength(500)]
    public string? Username { get; set; }

    /// <summary>
    ///   Optional password for mailbox authentication. Null for the system default mailbox that uses app configuration.
    /// </summary>
    [MaxLength(500)]
    public string? Password { get; set; }

    /// <summary>
    ///   If true, this mailbox is ignored by fetching jobs.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    ///   The last time old messages were cleaned up (e.g. deleting messages older than a year).
    ///   Used to ensure clean-up runs at most once per day.
    /// </summary>
    public DateTime? LastCleanUtc { get; set; }

    /// <summary>
    ///   Timestamp of the last time the inbox was read (e.g. last polling time).
    /// </summary>
    public DateTime? LastReadEmailUtc { get; set; }

    /// <summary>
    ///   Timestamp of the last time a new message was received (as detected by the fetch job).
    /// </summary>
    public DateTime? LastReceivedEmailUtc { get; set; }
}
