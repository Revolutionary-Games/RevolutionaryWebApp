using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;

    [Index(new[] { nameof(Name), nameof(ParentId) }, IsUnique = true)]
    [Index(nameof(AllowParentless))]
    [Index(nameof(OwnerId))]

    // TODO: is this a duplicate index that is not needed?
    [Index(nameof(ParentId))]
    public class StorageItem : UpdateableModel, IOwneableModel
    {
        // TODO: is there a threat from timing attack trying to enumerate existing files?
        public string Name { get; set; }

        // TODO: change to required
        public FileType Ftype { get; set; }
        public bool Special { get; set; } = false;

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

        public Task<StorageItemVersion> GetHighestVersion(ApplicationDbContext database)
        {
            return database.StorageItemVersions.AsQueryable().Where(v => v.StorageItemId == Id)
                .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
        }
    }
}
