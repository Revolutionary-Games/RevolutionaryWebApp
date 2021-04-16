using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    [Index(nameof(Name), IsUnique = true)]
    [Index(nameof(Slug), IsUnique = true)]
    public class LfsProject : UpdateableModel, IUpdateNotifications
    {
        [AllowSortingBy]
        public string Name { get; set; }

        [AllowSortingBy]
        public string Slug { get; set; }

        [AllowSortingBy]
        public bool Public { get; set; } = true;

        public bool Deleted { get; set; } = false;

        [Required]
        public string RepoUrl { get; set; }

        public string CloneUrl { get; set; }

        [AllowSortingBy]
        public int? TotalObjectSize { get; set; }

        public int? TotalObjectCount { get; set; }
        public DateTime? TotalSizeUpdated { get; set; }
        public DateTime? FileTreeUpdated { get; set; }
        public string FileTreeCommit { get; set; }

        public ICollection<LfsObject> LfsObjects { get; set; } = new HashSet<LfsObject>();
        public ICollection<ProjectGitFile> ProjectGitFiles { get; set; } = new HashSet<ProjectGitFile>();

        public LFSProjectInfo GetInfo()
        {
            return new()
            {
                Id = Id,
                Name = Name,
                Slug = Slug,
                Public = Public,
                TotalObjectSize = TotalObjectSize ?? 0,
                UpdatedAt = UpdatedAt,
                CreatedAt = CreatedAt
            };
        }

        public LFSProjectDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                Name = Name,
                Slug = Slug,
                Public = Public,
                Deleted = Deleted,
                TotalObjectSize = TotalObjectSize ?? 0,
                TotalObjectCount = TotalObjectCount ?? 0,
                TotalSizeUpdated = TotalSizeUpdated,
                FileTreeUpdated = FileTreeUpdated,
                FileTreeCommit = FileTreeCommit,
                RepoUrl = RepoUrl,
                CloneUrl = CloneUrl,
                UpdatedAt = UpdatedAt,
                CreatedAt = CreatedAt,
                LfsUrlSuffix = $"/api/v1/lfs/{Slug}"
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            // Skip sending normal updates if this is in deleted state (and didn't currently become undeleted
            // or deleted)
            if (entityState != EntityState.Modified || !Deleted)
            {
                var listGroup = Public ? NotificationGroups.LFSListUpdated : NotificationGroups.PrivateLFSUpdated;
                yield return new Tuple<SerializedNotification, string>(new LFSListUpdated
                        { Type = entityState.ToChangeType(), Item = GetInfo() },
                    listGroup);
            }

            // TODO: should there be a separate groups for private and deleted items as if someone joins the
            // notification group before this goes into a state where they couldn't join anymore, they still receive
            // notifications and that leaks some information
            yield return new Tuple<SerializedNotification, string>(
                new LFSProjectUpdated { Item = GetDTO() },
                NotificationGroups.LFSItemUpdatedPrefix + Id);
        }
    }
}
