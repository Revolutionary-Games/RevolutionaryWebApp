namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using SharedBase.Utilities;

/// <summary>
///   A logged user performed action
/// </summary>
[Index(nameof(PerformedById))]
public class ActionLogEntry : BaseModel
{
    public ActionLogEntry(string message)
    {
        Message = message;
    }

    public ActionLogEntry(string message, string? extendedDescription) : this(message)
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

    [AllowSortingBy]
    public long? PerformedById { get; set; }

    public User? PerformedBy { get; set; }

    public ActionLogEntryDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            Message = Message,
            CreatedAt = CreatedAt,
            PerformedById = PerformedById,
        };
    }
}
