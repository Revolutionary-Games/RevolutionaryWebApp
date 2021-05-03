using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System;
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
        public string Name { get; set; }

        // TODO: change to required
        public FileType Ftype { get; set; }
        public bool Special { get; set; } = false;

        [AllowSortingBy]
        public int? Size { get; set; }

        // TODO: change to required
        public FileAccess ReadAccess { get; set; } = FileAccess.Developer;

        public FileAccess WriteAccess { get; set; } = FileAccess.Developer;

        public long? OwnerId { get; set; }
        public User Owner { get; set; }

        public long? ParentId { get; set; }
        public StorageItem Parent { get; set; }
        public bool AllowParentless { get; set; } = false;

        // TODO: can this be named something else? This is the children of this item
        public ICollection<StorageItem> Children { get; set; } = new HashSet<StorageItem>();
        public ICollection<StorageItemVersion> StorageItemVersions { get; set; } = new HashSet<StorageItemVersion>();

        // Things that can reference this
        public ICollection<DehydratedObject> DehydratedObjects { get; set; } = new HashSet<DehydratedObject>();
        public ICollection<DevBuild> DevBuilds { get; set; } = new HashSet<DevBuild>();
        public ICollection<CiJobArtifact> CiJobArtifacts { get; set; } = new HashSet<CiJobArtifact>();

        public static Task<StorageItem> GetDevBuildsFolder(ApplicationDbContext database)
        {
            return database.StorageItems.AsQueryable()
                .FirstAsync(i => i.ParentId == null && i.Name == "DevBuild files");
        }

        public static async Task<StorageItem> GetDehydratedFolder(ApplicationDbContext database)
        {
            var devbuilds = await GetDevBuildsFolder(database);

            return await database.StorageItems.AsQueryable()
                .FirstAsync(i => i.ParentId == devbuilds.Id && i.Name == "Objects");
        }

        public static async Task<StorageItem> GetDevBuildBuildsFolder(ApplicationDbContext database)
        {
            var devbuilds = await GetDevBuildsFolder(database);

            return await database.StorageItems.AsQueryable()
                .FirstAsync(i => i.ParentId == devbuilds.Id && i.Name == "Dehydrated");
        }

        public bool IsReadableBy(User user)
        {
            return ReadAccess.IsAccessibleTo(user?.ComputeAccessLevel(), user?.Id, OwnerId);
        }

        public bool IsWritableBy(User user)
        {
            // Special files aren't writable by anyone
            return WriteAccess.IsAccessibleTo(user?.ComputeAccessLevel(), user?.Id, OwnerId) && !Special;
        }

        public Task<StorageItemVersion> GetHighestVersion(ApplicationDbContext database)
        {
            return database.StorageItemVersions.AsQueryable().Where(v => v.StorageItemId == Id)
                .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
        }

        public Task<StorageItemVersion> GetHighestUploadedVersion(ApplicationDbContext database)
        {
            return database.StorageItemVersions.AsQueryable().Include(v => v.StorageFile)
                .Where(v => v.StorageItemId == Id && v.Uploading != true)
                .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
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

            string parent = ParentId != null ? ParentId.ToString() : "root";

            if (ReadAccess == FileAccess.Public)
            {
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                {
                    Type = type,
                    Item = info
                }, NotificationGroups.FolderContentsUpdatedPublicPrefix + parent);
            }
            else if (ReadAccess == FileAccess.User)
            {
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                {
                    Type = type,
                    Item = info
                }, NotificationGroups.FolderContentsUpdatedUserPrefix + parent);
            }
            else if (ReadAccess == FileAccess.Developer)
            {
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                {
                    Type = type,
                    Item = info
                }, NotificationGroups.FolderContentsUpdatedDeveloperPrefix + parent);
            }
            else if (ReadAccess == FileAccess.OwnerOrAdmin)
            {
                yield return new Tuple<SerializedNotification, string>(new FolderContentsUpdated()
                {
                    Type = type,
                    Item = info
                }, NotificationGroups.FolderContentsUpdatedOwnerPrefix + parent);
            }

            yield return new Tuple<SerializedNotification, string>(new StorageItemUpdated()
            {
                Item = GetDTO()
            }, NotificationGroups.StorageItemUpdatedPrefix + Id);
        }
    }
}
