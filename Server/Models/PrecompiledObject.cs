namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Specified a type of a precompiled object that can be uploaded to the devcenter. The actual downloadables related
///   to this are contained as <see cref="PrecompiledObjectVersion"/>
/// </summary>
[Index(nameof(Name), IsUnique = true)]
public class PrecompiledObject : UpdateableModel, IUpdateNotifications, ISoftDeletable,
    IDTOCreator<PrecompiledObjectDTO>,
    IInfoCreator<PrecompiledObjectInfo>
{
    public PrecompiledObject(string name)
    {
        Name = name;
    }

    [Required]
    [AllowSortingBy]
    public string Name { get; set; }

    public long TotalStorageSize { get; set; }

    /// <summary>
    ///   Non-public are only visible to developers
    /// </summary>
    public bool Public { get; set; } = true;

    /// <summary>
    ///   When soft deleted no new things can be uploaded or downloaded to this object
    /// </summary>
    public bool Deleted { get; set; }

    public ICollection<PrecompiledObjectVersion> Versions { get; set; } = new HashSet<PrecompiledObjectVersion>();

    public PrecompiledObjectDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            TotalStorageSize = TotalStorageSize,
            Public = Public,
            Deleted = Deleted,
        };
    }

    public PrecompiledObjectInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            TotalStorageSize = TotalStorageSize,
            Public = Public,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // Skip sending normal updates if this is in deleted state (and didn't currently become undeleted
        // or deleted)
        if (entityState != EntityState.Modified || !Deleted)
        {
            var listGroup = Public ?
                NotificationGroups.PrecompiledObjectListUpdated :
                NotificationGroups.PrivatePrecompiledObjectUpdated;

            yield return new Tuple<SerializedNotification, string>(new PrecompiledObjectListUpdated
                { Type = entityState.ToChangeType(), Item = GetInfo() }, listGroup);
        }

        // TODO: should there be a separate groups for private and deleted items
        yield return new Tuple<SerializedNotification, string>(
            new PrecompiledObjectUpdated { Item = GetDTO() },
            NotificationGroups.PrecompiledObjectUpdatedPrefix + Id);
    }
}
