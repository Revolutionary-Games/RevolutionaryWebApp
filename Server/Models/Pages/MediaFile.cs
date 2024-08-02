namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Models.Enums;

/// <summary>
///   Media file that is uploaded to be accessed through a CDN for a <see cref="VersionedPage"/> (this is a separate
///   thing as these don't have read access control in contrast to <see cref="StorageFile"/>
/// </summary>
[Index(nameof(StoragePath), IsUnique = true)]
[Index(nameof(Name), nameof(FolderId), IsUnique = true)]
public class MediaFile : UpdateableModel, ISoftDeletable
{
    public MediaFile(string name, string storagePath, MediaFolder folder, User? uploadedBy)
    {
        if (name.Contains('/'))
            throw new ArgumentException("Name shouldn't contain a slash");

        Name = name;
        StoragePath = storagePath;
        Folder = folder;
        FolderId = folder.Id;
        UploadedBy = uploadedBy;
        UploadedById = uploadedBy?.Id;
    }

    [MaxLength(160)]
    public string Name { get; set; }

    [MaxLength(255)]
    public string StoragePath { get; set; }

    public long FolderId { get; set; }

    public MediaFolder Folder { get; set; }

    /// <summary>
    ///   Additional users who can modify this file (besides the uploader / last modifier)
    /// </summary>
    public GroupType ModifyAccess { get; set; } = GroupType.Admin;

    public long? UploadedById { get; set; }

    public User? UploadedBy { get; set; }

    public long? LastModifiedById { get; set; }

    public User? LastModifiedBy { get; set; }

    public bool Deleted { get; set; }

    public static async Task<string> GenerateStoragePath(ApplicationDbContext database, MediaFolder parentFolder,
        string name)
    {
        if (name.Contains('/'))
            throw new ArgumentException("Name shouldn't contain a slash");

        throw new NotImplementedException();
    }
}
