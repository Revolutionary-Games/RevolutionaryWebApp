namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations.Schema;
using DevCenterCommunication.Models;
using DevCenterCommunication.Models.Enums;
using Shared;
using SharedBase.Models;

/// <summary>
///   Version of a precompiled object for specific platform (and tags). This is actually what is downloadable from a
///   precompiled object.
/// </summary>
public class PrecompiledObjectVersion
{
    public PrecompiledObjectVersion(long ownedById, string version)
    {
        OwnedById = ownedById;
        Version = version;
    }

    public long OwnedById { get; set; }

    public PrecompiledObject OwnedBy { get; set; } = null!;

    [AllowSortingBy]
    public string Version { get; set; }

    [AllowSortingBy]
    public PackagePlatform Platform { get; set; }

    /// <summary>
    ///   Simple extra info on top of the version. For example if this is the debug version
    /// </summary>
    public PrecompiledTag Tags { get; set; } = PrecompiledTag.None;

    public bool Uploaded { get; set; }

    public long Size { get; set; }

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///   Used to track what is unused to allow auto-deleting. Only updated once per minute.
    /// </summary>
    [AllowSortingBy]
    public DateTime? LastDownload { get; set; }

    public long StoredInItemId { get; set; }
    public StorageItem StoredInItem { get; set; } = null!;

    public long? CreatedById { get; set; }
    public User? CreatedBy { get; set; }

    [NotMapped]
    public string StorageFileName => $"{OwnedById}:{Platform}:{Tags}:{Version}.br";

    public PrecompiledObjectVersionDTO GetDTO(bool loggedIn)
    {
        return new()
        {
            OwnedById = OwnedById,
            Version = Version,
            Platform = Platform,
            Tags = Tags,
            Uploaded = Uploaded,
            Size = Size,
            CreatedAt = CreatedAt,
            LastDownload = LastDownload,
            StoredInItemId = StoredInItemId,
            CreatedById = loggedIn ? CreatedById : -1,
        };
    }
}
