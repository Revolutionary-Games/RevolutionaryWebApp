namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using Shared.Converters;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(HashedKeyCode), IsUnique = true)]
public class AccessKey : UpdateableModel, IContainsHashedLookUps, IUpdateNotifications
{
    [Required]
    [AllowSortingBy]
    public string Description { get; set; } = string.Empty;

    [AllowSortingBy]
    public DateTime? LastUsed { get; set; }

    [AllowSortingBy]
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? LastUsedFrom { get; set; }

    [Required]
    [HashedLookUp]
    public string KeyCode { get; set; } = string.Empty;

    public string? HashedKeyCode { get; set; }

    [AllowSortingBy]
    public AccessKeyType KeyType { get; set; }

    public AccessKeyDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Description = Description,
            LastUsed = LastUsed,
            LastUsedFrom = LastUsedFrom,
            KeyType = KeyType,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new AccessKeyListUpdated
                { Type = entityState.ToChangeType(), Item = GetDTO() },
            NotificationGroups.AccessKeyListUpdated);
    }
}