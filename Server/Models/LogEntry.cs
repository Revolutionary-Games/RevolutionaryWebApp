namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Shared;
using Shared.Models;

/// <summary>
///   Important automated (non-messed) with log messages
/// </summary>
public class LogEntry : BaseModel
{
    [Required]
    public string Message { get; set; } = string.Empty;

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
