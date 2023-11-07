namespace ThriveDevCenter.Server.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Models;

/// <summary>
///   Specified a type of a precompiled object that can be uploaded to the devcenter. The actual downloadables related
///   to this are contained as <see cref="PrecompiledObjectVersion"/>
/// </summary>
[Index(nameof(Name), IsUnique = true)]
public class PrecompiledObject : UpdateableModel, ISoftDeletable, IDTOCreator<PrecompiledObjectDTO>,
    IInfoCreator<PrecompiledObjectInfo>
{
    public PrecompiledObject(string name)
    {
        Name = name;
    }

    [Required]
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
}
