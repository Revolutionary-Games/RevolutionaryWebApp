namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Interfaces;
using Shared.Models;

/// <summary>
///   A durable notification shown in a user's mailbox.
/// </summary>
public class UserNotification : IDTOCreator<UserNotificationDTO>
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;

    [MaxLength(200)]
    public string DeduplicationKey { get; set; } = string.Empty;

    public UserNotificationType Type { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public int CombinedCount { get; set; } = 1;

    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string TargetPath { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? BatchKey { get; set; }

    public long? CombinedIntoId { get; set; }
    public UserNotification? CombinedInto { get; set; }

    [Timestamp]
    public uint Xmin { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    public DateTime? EmailSentAt { get; set; }

    public UserNotificationDTO GetDTO()
    {
        return new UserNotificationDTO
        {
            Id = Id,
            Type = Type,
            Priority = Priority,
            CombinedCount = CombinedCount,
            Message = Message,
            TargetPath = TargetPath,
            CreatedAt = CreatedAt,
            ReadAt = ReadAt,
        };
    }
}
