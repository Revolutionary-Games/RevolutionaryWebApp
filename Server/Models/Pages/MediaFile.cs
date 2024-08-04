namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Models.Enums;
using Shared.Models.Pages;

/// <summary>
///   Media file that is uploaded to be accessed through a CDN for a <see cref="VersionedPage"/> (this is a separate
///   thing as these don't have read access control in contrast to <see cref="StorageFile"/>
/// </summary>
[Index(nameof(GlobalId), IsUnique = true)]
[Index(nameof(Name), nameof(FolderId), IsUnique = true)]
public class MediaFile : UpdateableModel, ISoftDeletable
{
    public MediaFile(string name, Guid globalId, MediaFolder folder, User? uploadedBy)
    {
        if (name.Contains('/'))
            throw new ArgumentException("Name shouldn't contain a slash");

        if (!name.Contains('.') || name.EndsWith('.'))
            throw new ArgumentException("Name should have an extension");

        Name = name;
        GlobalId = globalId;
        Folder = folder;
        FolderId = folder.Id;
        UploadedBy = uploadedBy;
        UploadedById = uploadedBy?.Id;
    }

    /// <summary>
    ///   Name of the file. May not be changed after upload, otherwise links will break
    /// </summary>
    [MaxLength(100)]
    public string Name { get; set; }

    public Guid GlobalId { get; set; }

    public long FolderId { get; set; }

    public MediaFolder Folder { get; set; }

    /// <summary>
    ///   Additional groups of users who can see this item in folder listings etc. All media assets are publicly
    ///   downloadable if the name is known
    /// </summary>
    public GroupType MetadataVisibility { get; set; } = GroupType.SystemOnly;

    /// <summary>
    ///   Additional users who can modify this file (besides the uploader / last modifier)
    /// </summary>
    public GroupType ModifyAccess { get; set; } = GroupType.Admin;

    public long? UploadedById { get; set; }

    public User? UploadedBy { get; set; }

    public long? LastModifiedById { get; set; }

    public User? LastModifiedBy { get; set; }

    /// <summary>
    ///   All image sizes are only uploaded after the image is processed (so this isn't fully considered uploaded until
    ///   then and may be purged as a failed upload)
    /// </summary>
    public bool Processed { get; set; }

    public bool Deleted { get; set; }

    public string GetStoragePath(MediaFileSize size)
    {
        var extension = Path.GetExtension(Name);

        var start = $"{GlobalId}/{Path.GetFileNameWithoutExtension(Name)}";

        string sizeText;

        switch (size)
        {
            case MediaFileSize.Original:
                sizeText = string.Empty;
                break;
            case MediaFileSize.Large:
                sizeText = "_large";
                break;
            case MediaFileSize.FitPage:
                sizeText = "_fit";
                break;
            case MediaFileSize.Thumbnail:
                sizeText = "_thumb";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(size), size, null);
        }

        return start + sizeText + extension;
    }
}
