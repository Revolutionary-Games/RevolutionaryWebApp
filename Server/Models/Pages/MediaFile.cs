namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;
using Shared.Notifications;
using Shared.Utilities;
using Utilities;

/// <summary>
///   Media file that is uploaded to be accessed through a CDN for a <see cref="VersionedPage"/> (this is a separate
///   thing as these don't have read access control in contrast to <see cref="StorageFile"/>
/// </summary>
[Index(nameof(GlobalId), IsUnique = true)]
[Index(nameof(Name), nameof(FolderId), IsUnique = true)]
public class MediaFile : UpdateableModel, IMediaFileInfo, ISoftDeletable, IUpdateNotifications
{
    public MediaFile(string name, Guid globalId, long folderId, long? uploadedById)
    {
        if (name.Contains('/'))
            throw new ArgumentException("Name shouldn't contain a slash");

        if (!name.Contains('.') || name.EndsWith('.'))
            throw new ArgumentException("Name should have an extension");

        Name = name;
        GlobalId = globalId;
        FolderId = folderId;
        UploadedById = uploadedById;
    }

    /// <summary>
    ///   Name of the file. May not be changed after upload, unless also the storage item in S3 is moved. Links to the
    ///   original size will become useless (if they use the full name, which hopefully won't get used)
    /// </summary>
    [MaxLength(100)]
    [AllowSortingBy]
    public string Name { get; set; }

    public Guid GlobalId { get; set; }

    [UpdateFromClientRequest]
    public long FolderId { get; set; }

    public MediaFolder Folder { get; set; } = null!;

    /// <summary>
    ///   Size of the original file uploaded by the user (that is then processed to get the other size variants)
    /// </summary>
    public long OriginalFileSize { get; set; }

    /// <summary>
    ///   Additional groups of users who can see this item in folder listings etc. All media assets are publicly
    ///   downloadable if the name is known
    /// </summary>
    [UpdateFromClientRequest]
    public GroupType MetadataVisibility { get; set; } = GroupType.SystemOnly;

    /// <summary>
    ///   Additional users who can modify this file (besides the uploader / last modifier)
    /// </summary>
    [UpdateFromClientRequest]
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

    public ICollection<SiteLayoutPart> UsedInSiteParts { get; set; } = new HashSet<SiteLayoutPart>();

    /// <summary>
    ///   Generic usages that can come from quite many different sources, so for simplicity of querying this is a
    ///   combined table
    /// </summary>
    public ICollection<MediaFileUsage> Usages { get; set; } = new HashSet<MediaFileUsage>();

    public string? GetStoragePath(MediaFileSize size)
    {
        if (Deleted || !Processed)
            return null;

        return MediaFileExtensions.GetStoragePath(this, size);
    }

    public string GetUploadPath()
    {
        var extension = Path.GetExtension(Name);

        return $"@mediaUpload/{GlobalId}{extension}";
    }

    public string GetIntermediateProcessingPath()
    {
        return $"@processing/{GlobalId}:{Id}";
    }

    public MediaFileInfo GetInfo()
    {
        return new MediaFileInfo
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            GlobalId = GlobalId,
            MetadataVisibility = MetadataVisibility,
            ModifyAccess = ModifyAccess,
            UploadedById = UploadedById,
            Processed = Processed,
            Deleted = Deleted,
        };
    }

    public MediaFileDTO GetDTO()
    {
        return new MediaFileDTO
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            GlobalId = GlobalId,
            MetadataVisibility = MetadataVisibility,
            ModifyAccess = ModifyAccess,
            UploadedById = UploadedById,
            LastModifiedById = LastModifiedById,
            Processed = Processed,
            Deleted = Deleted,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new MediaFileUpdated
            {
                Item = GetDTO(),
            },
            NotificationGroups.MediaFileUpdatedPrefix + Id);

        // To avoid having a bunch of access-checked update groups, there's now just a general info about the
        // parent folder having its items updated
        yield return new Tuple<SerializedNotification, string>(new MediaFolderContentsUpdated
            {
                FolderId = FolderId,
            },
            NotificationGroups.MediaFolderContentsUpdatedPrefix + FolderId);
    }
}
