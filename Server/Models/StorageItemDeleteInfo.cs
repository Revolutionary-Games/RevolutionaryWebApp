namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models.Enums;
using Shared.Models;

/// <summary>
///   Stores extra data about a file that was deleted to facilitate restoring the item
/// </summary>
public class StorageItemDeleteInfo
{
    /// <summary>
    ///   Constructor for creating a new object of this class, the other constructor is for the database to use
    /// </summary>
    public StorageItemDeleteInfo(StorageItem forItem, string originalFolderPath)
    {
        StorageItem = forItem;
        OriginalReadAccess = forItem.ReadAccess;
        OriginalWriteAccess = forItem.WriteAccess;

        OriginalFolderPath = originalFolderPath;

        if (forItem.Parent != null)
        {
            OriginalFolder = forItem.Parent;
            OriginalFolderReadAccess = forItem.Parent.ReadAccess;
            OriginalFolderWriteAccess = forItem.Parent.WriteAccess;
            OriginalFolderImportant = forItem.Parent.Important;
            OriginalFolderModificationLocked = forItem.Parent.ModificationLocked;
            OriginalFolderOwner = forItem.Parent.Owner;
        }
    }

    public StorageItemDeleteInfo(long storageItemId, FileAccess originalReadAccess, FileAccess originalWriteAccess,
        string originalFolderPath)
    {
        StorageItemId = storageItemId;
        OriginalReadAccess = originalReadAccess;
        OriginalWriteAccess = originalWriteAccess;
        OriginalFolderPath = originalFolderPath;
    }

    [Key]
    public long StorageItemId { get; set; }

    public StorageItem? StorageItem { get; set; }

    /// <summary>
    ///   The time this item was deleted at, used to track when the item needs to be permanently purged
    /// </summary>
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;

    public long? OriginalFolderId { get; set; }

    /// <summary>
    ///   The original folder the item was deleted from. If the folder was deleted then this will be null and the path
    ///   needs to be relied on.
    /// </summary>
    public StorageItem? OriginalFolder { get; set; }

    /// <summary>
    ///   Saves the original read access as when in deleted state the file needs to be modified for the trash folder
    /// </summary>
    public FileAccess OriginalReadAccess { get; set; }

    public FileAccess OriginalWriteAccess { get; set; }

    /// <summary>
    ///   Id of the user who deleted this item
    /// </summary>
    public long? DeletedById { get; set; }

    public User? DeletedBy { get; set; }

    /// <summary>
    ///   The original path the item was at, can be used if <see cref="StorageItem"/> is null. If this is empty the
    ///   item was at the root folder
    /// </summary>
    public string OriginalFolderPath { get; set; }

    // Properties of the original folder that are needed to recreate it
    public FileAccess OriginalFolderReadAccess { get; set; }
    public FileAccess OriginalFolderWriteAccess { get; set; }
    public bool OriginalFolderImportant { get; set; }
    public bool OriginalFolderModificationLocked { get; set; }

    public long? OriginalFolderOwnerId { get; set; }
    public User? OriginalFolderOwner { get; set; }

    public StorageItemDeleteInfoDTO GetDTO()
    {
        return new()
        {
            StorageItemId = StorageItemId,
            DeletedAt = DeletedAt,
            OriginalPath = OriginalFolderPath,
            DeletedById = DeletedById,
        };
    }
}
