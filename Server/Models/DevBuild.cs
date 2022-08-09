using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using SharedBase.ModelVerifiers;
    using Utilities;

    [Index(new[] { nameof(BuildHash), nameof(Platform) }, IsUnique = true)]
    [Index(nameof(Anonymous))]
    [Index(nameof(StorageItemId))]
    [Index(nameof(VerifiedById))]
    [Index(nameof(Platform))]
    [Index(nameof(Branch))]
    public class DevBuild : UpdateableModel, IUpdateNotifications
    {
        [Required]
        public string BuildHash { get; set; } = string.Empty;

        [Required]
        [AllowSortingBy]
        public string Platform { get; set; } = string.Empty;

        [Required]
        [AllowSortingBy]
        public string Branch { get; set; } = string.Empty;

        [Required]
        public string BuildZipHash { get; set; } = string.Empty;

        [NotNullOrEmptyIf(BooleanPropertyIsTrue = nameof(BuildOfTheDay))]
        public string? Description { get; set; }

        public int Score { get; set; } = 0;
        public int Downloads { get; set; } = 0;

        public bool Important { get; set; } = false;
        public bool Keep { get; set; } = false;

        public string? PrUrl { get; set; }
        public bool PrFetched { get; set; } = false;

        public bool BuildOfTheDay { get; set; } = false;

        public bool Anonymous { get; set; } = false;
        public bool Verified { get; set; } = false;
        public long? VerifiedById { get; set; }
        public User? VerifiedBy { get; set; }

        public long StorageItemId { get; set; }
        public StorageItem? StorageItem { get; set; }

        /// <summary>
        ///   The dehydrated objects that are required by this DevBuild. This is used to cleanup dehydrated objects
        ///   that are no longer needed
        /// </summary>
        public ICollection<DehydratedObject> DehydratedObjects { get; set; } = new HashSet<DehydratedObject>();

        public async Task<bool> IsUploaded(ApplicationDbContext database)
        {
            if (StorageItem == null)
                throw new NotLoadedModelNavigationException();

            var version = await StorageItem.GetHighestVersion(database);

            if (version == null)
                return false;

            return !version.Uploading;
        }

        public DevBuildDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                BuildHash = BuildHash,
                Platform = Platform,
                Branch = Branch,
                BuildZipHash = BuildZipHash,
                Description = Description,
                Score = Score,
                Downloads = Downloads,
                Important = Important,
                Keep = Keep,
                BuildOfTheDay = BuildOfTheDay,
                Anonymous = Anonymous,
                Verified = Verified,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
            };
        }

        public DevBuildLauncherDTO GetLauncherDTO()
        {
            return new()
            {
                Id = Id,
                BuildHash = BuildHash,
                Platform = Platform,
                Branch = Branch,
                BuildZipHash = BuildZipHash,
                Description = Description,
                Score = Score,
                Downloads = Downloads,
                Important = Important,
                Keep = Keep,
                BuildOfTheDay = BuildOfTheDay,
                Anonymous = Anonymous,
                Verified = Verified,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            var dto = GetDTO();

            yield return new Tuple<SerializedNotification, string>(new DevBuildListUpdated()
            {
                Type = entityState.ToChangeType(),

                // TODO: create a separate type for use with the list
                Item = dto,
            }, NotificationGroups.DevBuildsListUpdated);

            yield return new Tuple<SerializedNotification, string>(new DevBuildUpdated()
            {
                Item = dto,
            }, NotificationGroups.DevBuildUpdatedPrefix + Id);
        }
    }
}
