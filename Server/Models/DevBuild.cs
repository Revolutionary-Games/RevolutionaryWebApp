using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using ModelVerifiers;

    [Index(new[] { nameof(BuildHash), nameof(Platform) }, IsUnique = true)]
    [Index(nameof(Anonymous))]
    [Index(nameof(StorageItemId))]
    [Index(nameof(VerifiedById))]
    public class DevBuild : UpdateableModel
    {
        [Required]
        public string BuildHash { get; set; }

        [Required]
        public string Platform { get; set; }

        [Required]
        public string Branch { get; set; }

        [Required]
        public string BuildZipHash { get; set; }

        [NotNullOrEmptyIf(BooleanPropertyIsTrue = nameof(BuildOfTheDay))]
        public string Description { get; set; }

        public int Score { get; set; } = 0;
        public int Downloads { get; set; } = 0;

        public bool Important { get; set; } = false;
        public bool Keep { get; set; } = false;

        public string PrUrl { get; set; }
        public bool PrFetched { get; set; } = false;

        public bool BuildOfTheDay { get; set; } = false;

        public bool Anonymous { get; set; } = false;
        public bool Verified { get; set; } = false;
        public long? VerifiedById { get; set; }
        public User VerifiedBy { get; set; }

        public long StorageItemId { get; set; }
        public StorageItem StorageItem { get; set; }

        /// <summary>
        ///   The dehydrated objects that are required by this DevBuild. This is used to cleanup dehydrated objects
        ///   that are no longer needed
        /// </summary>
        public ICollection<DehydratedObject> DehydratedObjects { get; set; } = new HashSet<DehydratedObject>();

        public async Task<bool> IsUploaded(ApplicationDbContext database)
        {
            var version = await StorageItem.GetHighestVersion(database);

            if (version == null)
                return false;

            return !version.Uploading;
        }
    }
}
