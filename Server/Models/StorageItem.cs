namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using DevCenterCommunication.Models.Enums;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Notifications;
using Shared.Utilities;
using Utilities;
using FileAccess = DevCenterCommunication.Models.Enums.FileAccess;

[Index(nameof(Name), nameof(ParentId), IsUnique = true)]
[Index(nameof(AllowParentless))]
[Index(nameof(OwnerId))]

// TODO: is this a duplicate index that is not needed?
[Index(nameof(ParentId))]
public class StorageItem : UpdateableModel, IOwneableModel, IUpdateNotifications, ISoftDeletable
{
    // TODO: is there a threat from timing attack trying to enumerate existing files?
    [AllowSortingBy]
    [Required]
    public string Name { get; set; } = string.Empty;

    // TODO: rename this to "FileType"
    // ReSharper disable once IdentifierTypo
    public FileType Ftype { get; set; }
    public bool Special { get; set; }

    /// <summary>
    ///   Important files can't be deleted and their versions will all be kept
    /// </summary>
    public bool Important { get; set; }

    [AllowSortingBy]
    public long? Size { get; set; }

    public FileAccess ReadAccess { get; set; } = FileAccess.Developer;

    public FileAccess WriteAccess { get; set; } = FileAccess.Developer;

    /// <summary>
    ///   When set this item can't be modified itself. For a folder this means the folder can't be modified but it can
    ///   have new items added to it. For files this means that new versions can be uploaded but the file name and
    ///   other properties can't be modified on the file itself.
    /// </summary>
    public bool ModificationLocked { get; set; }

    /// <summary>
    ///   Deleted files are stored in the trash folder before they are permanently purged. When in deleted state
    ///   there's an extra data in <see cref="DeleteInfo"/>
    /// </summary>
    public bool Deleted { get; set; }

    /// <summary>
    ///   Stores the previous location this item was at. Allows undoing a file move
    /// </summary>
    public string? MovedFromLocation { get; set; }

    public long? OwnerId { get; set; }
    public User? Owner { get; set; }

    public long? LastModifiedById { get; set; }
    public User? LastModifiedBy { get; set; }

    public long? ParentId { get; set; }
    public StorageItem? Parent { get; set; }
    public bool AllowParentless { get; set; } = false;

    // TODO: can this be named something else? This is the children of this item
    public ICollection<StorageItem> Children { get; set; } = new HashSet<StorageItem>();
    public ICollection<StorageItemVersion> StorageItemVersions { get; set; } = new HashSet<StorageItemVersion>();

    // Things that can reference this
    public ICollection<DehydratedObject> DehydratedObjects { get; set; } = new HashSet<DehydratedObject>();
    public ICollection<DevBuild> DevBuilds { get; set; } = new HashSet<DevBuild>();
    public ICollection<CiJobArtifact> CiJobArtifacts { get; set; } = new HashSet<CiJobArtifact>();

    public ICollection<DebugSymbol> DebugSymbols { get; set; } = new HashSet<DebugSymbol>();

    /// <summary>
    ///   Info about where this item was deleted from to allow restoring this, even if the original folder was deleted
    /// </summary>
    public StorageItemDeleteInfo? DeleteInfo { get; set; }

    public ICollection<StorageItemDeleteInfo> OriginalFolderOfDeleted { get; set; } =
        new HashSet<StorageItemDeleteInfo>();

    public static Task<StorageItem> GetDevBuildsFolder(ApplicationDbContext database)
    {
        return database.StorageItems.FirstAsync(i => i.ParentId == null && i.Name == "DevBuild files");
    }

    public static async Task<StorageItem> GetDehydratedFolder(ApplicationDbContext database)
    {
        var devbuilds = await GetDevBuildsFolder(database);

        return await database.StorageItems.FirstAsync(i => i.ParentId == devbuilds.Id && i.Name == "Objects");
    }

    public static async Task<StorageItem> GetDevBuildBuildsFolder(ApplicationDbContext database)
    {
        var devbuilds = await GetDevBuildsFolder(database);

        return await database.StorageItems.FirstAsync(i => i.ParentId == devbuilds.Id && i.Name == "Dehydrated");
    }

    public static Task<StorageItem> GetSymbolsFolder(ApplicationDbContext database)
    {
        return database.StorageItems.FirstAsync(i => i.ParentId == null && i.Name == "Symbols");
    }

    public static Task<StorageItem> GetTrashFolder(ApplicationDbContext database)
    {
        return database.StorageItems.FirstAsync(i => i.ParentId == null && i.Name == "Trash");
    }

    public static async Task<StorageItem?> FindByPath(ApplicationDbContext database, string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            // Root folder is represented by null
            return null;
        }

        var pathParts = path.Split('/');

        StorageItem? currentItem = null;

        foreach (var part in pathParts)
        {
            // Skip empty parts to support starting with a slash or having multiple in a row
            if (string.IsNullOrEmpty(part))
                continue;

            // If we have already found a file, then further path parts are invalid
            if (currentItem?.Ftype == FileType.File)
                throw new ArgumentException("Detected further path components after a file was found");

            var currentId = currentItem?.Id;
            var nextItem =
                await database.StorageItems.FirstOrDefaultAsync(i => i.ParentId == currentId && i.Name == part);

            currentItem = nextItem ?? throw new ArgumentException($"Path part \"{part}\" doesn't exist");
        }

        return currentItem;
    }

    public bool IsReadableBy(User? user)
    {
        return ReadAccess.IsAccessibleTo(user?.AccessCachedGroupsOrThrow(), user?.Id, OwnerId);
    }

    public bool IsWritableBy(User? user)
    {
        if (!WriteAccess.IsAccessibleTo(user?.AccessCachedGroupsOrThrow(), user?.Id, OwnerId))
            return false;

        // Special files aren't writable by anyone, but special folders are writable
        if (Ftype == FileType.File)
            return !Special;

        return true;
    }

    public Task<StorageItemVersion?> GetHighestVersion(ApplicationDbContext database, bool includeDeleted = false)
    {
        if (includeDeleted)
        {
            return database.StorageItemVersions.Where(v => v.StorageItemId == Id)
                .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
        }

        return database.StorageItemVersions.Where(v => v.StorageItemId == Id && !v.Deleted)
            .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
    }

    public Task<StorageItemVersion?> GetHighestUploadedVersion(ApplicationDbContext database)
    {
        return database.StorageItemVersions.Include(v => v.StorageFile)
            .Where(v => v.StorageItemId == Id && v.Uploading != true && !v.Deleted)
            .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
    }

    public Task<StorageItemVersion?> GetLowestUploadedVersion(ApplicationDbContext database)
    {
        return database.StorageItemVersions.Include(v => v.StorageFile)
            .Where(v => v.StorageItemId == Id && v.Uploading != true && !v.Deleted)
            .OrderBy(v => v.Version).FirstOrDefaultAsync();
    }

    public async Task<int> GetNextVersionNumber(ApplicationDbContext database)
    {
        var highest = await GetHighestVersion(database, true);

        if (highest == null)
            return 1;

        return highest.Version + 1;
    }

    /// <summary>
    ///   Creates the next StorageItemVersion for this object. Doesn't save the database
    /// </summary>
    public async Task<StorageItemVersion> CreateNextVersion(ApplicationDbContext database, User? uploader)
    {
        var number = await GetNextVersionNumber(database);

        var version = new StorageItemVersion
        {
            Version = number,
            StorageItemId = Id,
            UploadedById = uploader?.Id,
        };

        StorageItemVersions.Add(version);

        await database.StorageItemVersions.AddAsync(version);
        return version;
    }

    public async Task<string> ComputeStoragePath(ApplicationDbContext database)
    {
        string parentPath = string.Empty;

        if (ParentId != null)
        {
            var parent = Parent;

            if (parent == null)
            {
                parent = await database.StorageItems.FindAsync(ParentId);

                if (parent == null)
                    throw new NullReferenceException("failed to get the StorageItem parent for path calculation");
            }

            parentPath = await parent.ComputeStoragePath(database) + '/';
        }

        return parentPath + Name;
    }

    /// <summary>
    ///   Returns all the parent folders up to the root folder
    ///   (but doesn't return null representing the root folder)
    /// </summary>
    /// <param name="database">Where to load parents from</param>
    /// <returns>
    ///   Enumerator that results in all the parent folders starting from the immediate parent and going up the tree
    /// </returns>
    public async Task<IEnumerable<StorageItem>> GetParentsRecursively(NotificationsEnabledDb database)
    {
        var result = new List<StorageItem>();

        if (ParentId != null)
        {
            var parent = Parent;

            if (parent == null)
            {
                parent = await database.StorageItems.FindAsync(ParentId);

                if (parent == null)
                {
                    throw new NullReferenceException(
                        "failed to get the StorageItem parent for parent folder returning");
                }
            }

            result.Add(parent);
            result.AddRange(await parent.GetParentsRecursively(database));
        }

        return result;
    }

    /// <summary>
    ///   Makes sure the name of this item is unique within the parent
    /// </summary>
    /// <param name="database">Where to load the info on conflicts</param>
    /// <param name="parentFolderId">
    ///   If set uses this as the parent folder ID, otherwise the one in this object set currently is used
    /// </param>
    public async Task MakeNameUniqueInFolder(ApplicationDbContext database, long? parentFolderId = null)
    {
        parentFolderId ??= Parent?.Id ?? ParentId;

        if (!await database.StorageItems.AnyAsync(i => i.ParentId == parentFolderId && i.Name == Name))
            return;

        // TODO: if there is a truly huge number of names we'd have no other way than to query the DB each time
        var names = await database.StorageItems.Where(i => i.ParentId == parentFolderId).Select(i => i.Name)
            .ToListAsync();

        var oldName = Name;

        var extension = Path.GetExtension(oldName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(oldName);

        int suffixCounter = 1;

        // Detect an existing counter and resume it
        var nameParts = nameWithoutExtension.Split('_');

        if (nameParts.Length > 1 && int.TryParse(nameParts.Last(), out var parsed))
        {
            nameWithoutExtension = string.Join('_', nameParts.Take(nameParts.Length - 1));
            suffixCounter = parsed;
        }

        // TODO: if the names are checked from the DB this should be lowered to 50
        int attempts = 1000;

        while (names.Contains(Name))
        {
            if (--attempts <= 0)
                throw new InvalidOperationException("Could not generate a unique name, too many names were attempted");

            Name = $"{nameWithoutExtension}_{++suffixCounter}{extension}";
        }

        if (oldName == Name)
            throw new InvalidOperationException("Could not find a unique name, failed to detect original name as used");

        this.BumpUpdatedAt();
    }

    public StorageItemInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            Name = Name,
            Ftype = Ftype,
            Size = Size,
            ReadAccess = ReadAccess,
        };
    }

    public StorageItemDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            Name = Name,
            Ftype = Ftype,
            Special = Special,
            Important = Important,
            Size = Size,
            ReadAccess = ReadAccess,
            WriteAccess = WriteAccess,
            ModificationLocked = ModificationLocked,
            OwnerId = OwnerId,
            ParentId = ParentId,
            AllowParentless = AllowParentless,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            LastModifiedById = LastModifiedById,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        if (ReadAccess == FileAccess.Nobody)
            yield break;

        var info = GetInfo();
        var type = entityState.ToChangeType();

        string? parent = ParentId != null ? ParentId.ToString() : "root";

        switch (ReadAccess)
        {
            case FileAccess.Public:
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated
                {
                    Type = type,
                    Item = info,
                }, NotificationGroups.FolderContentsUpdatedPublicPrefix + parent);

                break;
            case FileAccess.RestrictedUser:
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated
                {
                    Type = type,
                    Item = info,
                }, NotificationGroups.FolderContentsUpdatedRestrictedUserPrefix + parent);

                break;
            case FileAccess.User:
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated
                {
                    Type = type,
                    Item = info,
                }, NotificationGroups.FolderContentsUpdatedUserPrefix + parent);

                break;
            case FileAccess.Developer:
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated
                {
                    Type = type,
                    Item = info,
                }, NotificationGroups.FolderContentsUpdatedDeveloperPrefix + parent);

                break;
            case FileAccess.OwnerOrAdmin:
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated
                {
                    Type = type,
                    Item = info,
                }, NotificationGroups.FolderContentsUpdatedOwnerPrefix + parent);

                break;
        }

        yield return new Tuple<SerializedNotification, string>(new StorageItemUpdated
        {
            Item = GetDTO(),
        }, NotificationGroups.StorageItemUpdatedPrefix + Id);
    }

    public override string ToString()
    {
        return $"{Id} ({Name})";
    }
}
