using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    [Index(new[] { nameof(Name), nameof(ParentId) }, IsUnique = true)]
    [Index(nameof(AllowParentless))]
    [Index(nameof(OwnerId))]

    // TODO: is this a duplicate index that is not needed?
    [Index(nameof(ParentId))]
    public class StorageItem : UpdateableModel, IOwneableModel, IUpdateNotifications
    {
        // TODO: is there a threat from timing attack trying to enumerate existing files?
        [AllowSortingBy]
        [Required]
        public string Name { get; set; } = string.Empty;

        // TODO: change to required
        // TODO: rename this to "FileType"
        // ReSharper disable once IdentifierTypo
        public FileType Ftype { get; set; }
        public bool Special { get; set; } = false;

        [AllowSortingBy]
        public long? Size { get; set; }

        public FileAccess ReadAccess { get; set; } = FileAccess.Developer;

        public FileAccess WriteAccess { get; set; } = FileAccess.Developer;

        public long? OwnerId { get; set; }
        public User? Owner { get; set; }

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
            return ReadAccess.IsAccessibleTo(user?.ComputeAccessLevel(), user?.Id, OwnerId);
        }

        public bool IsWritableBy(User? user)
        {
            if (!WriteAccess.IsAccessibleTo(user?.ComputeAccessLevel(), user?.Id, OwnerId))
                return false;

            // Special files aren't writable by anyone, but special folders are writable
            if (Ftype == FileType.File)
                return !Special;

            return true;
        }

        public Task<StorageItemVersion?> GetHighestVersion(ApplicationDbContext database)
        {
            return database.StorageItemVersions.Where(v => v.StorageItemId == Id)
                .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
        }

        public Task<StorageItemVersion?> GetHighestUploadedVersion(ApplicationDbContext database)
        {
            return database.StorageItemVersions.Include(v => v.StorageFile)
                .Where(v => v.StorageItemId == Id && v.Uploading != true)
                .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
        }

        public Task<StorageItemVersion?> GetLowestUploadedVersion(ApplicationDbContext database)
        {
            return database.StorageItemVersions.Include(v => v.StorageFile)
                .Where(v => v.StorageItemId == Id && v.Uploading != true)
                .OrderBy(v => v.Version).FirstOrDefaultAsync();
        }

        public async Task<int> GetNextVersionNumber(ApplicationDbContext database)
        {
            var highest = await GetHighestVersion(database);

            if (highest == null)
                return 1;

            return highest.Version + 1;
        }

        /// <summary>
        ///   Creates the next StorageItemVersion for this object. Doesn't save the database
        /// </summary>
        public async Task<StorageItemVersion> CreateNextVersion(ApplicationDbContext database)
        {
            var number = await GetNextVersionNumber(database);

            var version = new StorageItemVersion()
            {
                Version = number,
                StorageItemId = Id
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
                        throw new NullReferenceException(
                            "failed to get the StorageItem parent for parent folder returning");
                }

                result.Add(parent);
                result.AddRange(await parent.GetParentsRecursively(database));
            }

            return result;
        }

        public StorageItemInfo GetInfo()
        {
            return new()
            {
                Id = Id,
                Name = Name,
                Ftype = Ftype,
                Size = Size,
                ReadAccess = ReadAccess
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
                Size = Size,
                ReadAccess = ReadAccess,
                WriteAccess = WriteAccess,
                OwnerId = OwnerId,
                ParentId = ParentId,
                AllowParentless = AllowParentless,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
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
                    yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                    {
                        Type = type,
                        Item = info
                    }, NotificationGroups.FolderContentsUpdatedPublicPrefix + parent);

                    break;
                case FileAccess.RestrictedUser:
                    yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                    {
                        Type = type,
                        Item = info
                    }, NotificationGroups.FolderContentsUpdatedRestrictedUserPrefix + parent);

                    break;
                case FileAccess.User:
                    yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                    {
                        Type = type,
                        Item = info
                    }, NotificationGroups.FolderContentsUpdatedUserPrefix + parent);

                    break;
                case FileAccess.Developer:
                    yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                    {
                        Type = type,
                        Item = info
                    }, NotificationGroups.FolderContentsUpdatedDeveloperPrefix + parent);

                    break;
                case FileAccess.OwnerOrAdmin:
                    yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                    {
                        Type = type,
                        Item = info
                    }, NotificationGroups.FolderContentsUpdatedOwnerPrefix + parent);

                    break;
            }

            yield return new Tuple<SerializedNotification, string>(new StorageItemUpdated()
            {
                Item = GetDTO()
            }, NotificationGroups.StorageItemUpdatedPrefix + Id);
        }
    }
}
