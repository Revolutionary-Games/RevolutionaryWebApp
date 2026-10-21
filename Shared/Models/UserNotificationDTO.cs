namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   A durable notification shown in the user's mailbox.
/// </summary>
public sealed class UserNotificationDTO
{
    public long Id { get; set; }

    public UserNotificationType Type { get; set; }

    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string TargetPath { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>
///   The number of unread mailbox notifications.
/// </summary>
public sealed class UnreadNotificationCountDTO
{
    public int Count { get; set; }
}
