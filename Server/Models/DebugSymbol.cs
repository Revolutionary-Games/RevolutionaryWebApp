namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(RelativePath), IsUnique = true)]
public class DebugSymbol : UpdateableModel, IUpdateNotifications
{
    // TODO: probably want to add an index for this
    [Required]
    [AllowSortingBy]
    public string Name { get; set; } = string.Empty;

    [Required]
    [AllowSortingBy]
    public string RelativePath { get; set; } = string.Empty;

    [UpdateFromClientRequest]
    public bool Active { get; set; }

    public bool Uploaded { get; set; }

    public long Size { get; set; }

    public long StoredInItemId { get; set; }
    public StorageItem? StoredInItem { get; set; }

    public long? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    [NotMapped]
    public string StorageFileName => RelativePath.Replace('/', '_');

    public DebugSymbolDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            RelativePath = RelativePath,
            Active = Active,
            Uploaded = Uploaded,
            Size = Size,
            StoredInItemId = StoredInItemId,
            CreatedById = CreatedById,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(
            new DebugSymbolListUpdated()
            {
                Type = entityState.ToChangeType(),
                Item = GetDTO(),
            }, NotificationGroups.SymbolListUpdated);
    }
}