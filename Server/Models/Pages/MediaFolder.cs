namespace RevolutionaryWebApp.Server.Models.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared.Models.Enums;

/// <summary>
///   Folder that groups <see cref="MediaFile"/> objects
/// </summary>
[Index(nameof(Name), nameof(ParentFolderId), IsUnique = true)]
public class MediaFolder : UpdateableModel
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
    public string Name { get; set; }

    public long? ParentFolderId { get; set; }

    public MediaFolder? ParentFolder { get; set; }

    public GroupType ContentWriteAccess { get; set; } = GroupType.Developer;

    public GroupType ContentReadAccess { get; set; } = GroupType.Developer;

    public GroupType SubFolderModifyAccess { get; set; } = GroupType.Admin;
    public GroupType FolderModifyAccess { get; set; } = GroupType.Admin;

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
}
