namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Folder that groups <see cref="MediaFile"/> objects
/// </summary>
[Index(nameof(Name), nameof(ParentFolderId), IsUnique = true)]
public class MediaFolder : UpdateableModel, IUpdateNotifications
{
    public const long WebsitePartsId = 1;
    public const long WebsitePagesId = 2;
    public const long WebsitePostsId = 3;
    public const long WikiImagesId = 4;
    public const long AvatarsId = 9;
    public const long UserUploadsId = 10;

    public MediaFolder(string name)
    {
        if (name.Contains('/'))
            throw new ArgumentException("Name shouldn't contain slashes");

        Name = name;
    }

    [MaxLength(80)]
    [AllowSortingBy]
    [UpdateFromClientRequest]
    public string Name { get; set; }

    public long? ParentFolderId { get; set; }

    public MediaFolder? ParentFolder { get; set; }

    [UpdateFromClientRequest]
    public GroupType ContentWriteAccess { get; set; } = GroupType.Developer;

    [UpdateFromClientRequest]
    public GroupType ContentReadAccess { get; set; } = GroupType.Developer;

    [UpdateFromClientRequest]
    public GroupType SubFolderModifyAccess { get; set; } = GroupType.Admin;

    [UpdateFromClientRequest]
    public GroupType FolderModifyAccess { get; set; } = GroupType.Admin;

    [UpdateFromClientRequest]
    public long? OwnedById { get; set; }

    public User? OwnedBy { get; set; }

    public long? LastModifiedById { get; set; }

    public User? LastModifiedBy { get; set; }

    /// <summary>
    ///   This bool just exists to make it easier to make folders disappear automatically once the items in them which
    ///   are deleted expire
    /// </summary>
    public bool DeleteIfEmpty { get; set; }

    public ICollection<MediaFolder> SubFolders { get; set; } = new HashSet<MediaFolder>();

    public ICollection<MediaFile> FolderItems { get; set; } = new HashSet<MediaFile>();

    public MediaFolderInfo GetInfo()
    {
        return new MediaFolderInfo
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            ParentFolderId = ParentFolderId,
            ContentWriteAccess = ContentWriteAccess,
            ContentReadAccess = ContentReadAccess,
            OwnedById = OwnedById,
            LastModifiedById = LastModifiedById,
        };
    }

    public MediaFolderDTO GetDTO()
    {
        return new MediaFolderDTO
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            ParentFolderId = ParentFolderId,
            ContentWriteAccess = ContentWriteAccess,
            ContentReadAccess = ContentReadAccess,
            SubFolderModifyAccess = SubFolderModifyAccess,
            FolderModifyAccess = FolderModifyAccess,
            OwnedById = OwnedById,
            LastModifiedById = LastModifiedById,
            DeleteIfEmpty = DeleteIfEmpty,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new MediaFolderUpdated
        {
            Item = GetDTO(),
        }, NotificationGroups.MediaFolderUpdatedPrefix + Id);

        // For now only send update notifications for non-root folder
        if (ParentFolderId != null)
        {
            // To avoid having a bunch of access checked update groups, there's now just a general info about the
            // parent folder having its items updated
            yield return new Tuple<SerializedNotification, string>(new MediaFolderContentsUpdated
            {
                FolderId = ParentFolderId.Value,
            }, NotificationGroups.MediaFolderContentsUpdatedPrefix + ParentFolderId);
        }
    }
}
