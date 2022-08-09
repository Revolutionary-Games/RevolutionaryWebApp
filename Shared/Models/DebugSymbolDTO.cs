namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class DebugSymbolDTO : ClientSideTimedModel, ICloneable
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string RelativePath { get; set; } = string.Empty;

    public bool Active { get; set; }
    public bool Uploaded { get; set; }
    public long Size { get; set; }
    public long StoredInItemId { get; set; }
    public long? CreatedById { get; set; }

    public object Clone()
    {
        return new DebugSymbolDTO
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
}