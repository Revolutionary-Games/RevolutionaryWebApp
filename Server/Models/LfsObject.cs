namespace ThriveDevCenter.Server.Models;

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared.Models;

[Index(new[] { nameof(LfsProjectId), nameof(LfsOid) }, IsUnique = true)]
public class LfsObject : UpdateableModel
{
    /// <summary>
    ///   The oid used in Git LFS protocol to identify this object.
    ///   For some reason "Oid" refuses to create column, so this is named like this.
    /// </summary>
    [Required]
    public string LfsOid { get; set; } = string.Empty;

    public long Size { get; set; }

    [Required]
    public string StoragePath { get; set; } = string.Empty;

    public long LfsProjectId { get; set; }
    public LfsProject? LfsProject { get; set; }

    public LfsObjectDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            LfsOid = LfsOid,
            Size = Size,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }
}