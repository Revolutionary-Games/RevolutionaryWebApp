namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Shared;
using Shared.Models;
using SharedBase.Utilities;

/// <summary>
///   Important automated (non-directly user interacted) with log messages
/// </summary>
public class LogEntry : BaseModel
{
    public LogEntry(string message)
    {
        Message = message;
    }

    public LogEntry(string message, string? extendedDescription) : this(message)
    {
        if (extendedDescription != null)
            Extended = extendedDescription.Truncate(AppInfo.MaxLogEntryExtraInfoLength);
    }

    [Required]
    public string Message { get; set; }

    /// <summary>
    ///   Extended description of the message, not shown by default
    /// </summary>
    [MaxLength(AppInfo.MaxLogEntryExtraInfoLength)]
    public string? Extended { get; set; }

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///   The user targeted in this entry (maybe null). This is implicitly indexed
    /// </summary>
    [AllowSortingBy]
    public long? TargetUserId { get; set; }

    public User? TargetUser { get; set; }

    public LogEntryDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            Message = Message,
            CreatedAt = CreatedAt,
            TargetUserId = TargetUserId,
        };
    }
}
