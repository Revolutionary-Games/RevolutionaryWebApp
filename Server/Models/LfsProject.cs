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

        public LFSProjectDTO GetDTO(string lfsBaseUrl)
        {
            return new()
            {
                Id = Id,
                Name = Name,
                Slug = Slug,
                Public = Public,
                TotalObjectSize = TotalObjectSize ?? 0,
                TotalObjectCount = TotalObjectCount ?? 0,
                TotalSizeUpdated = TotalSizeUpdated,
                FileTreeUpdated = FileTreeUpdated,
                FileTreeCommit = FileTreeCommit,
                RepoUrl = RepoUrl,
                CloneUrl = CloneUrl,
                UpdatedAt = UpdatedAt,
                CreatedAt = CreatedAt,
                LfsBaseUrl = lfsBaseUrl
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            var listGroup = Public ? NotificationGroups.LFSListUpdated : NotificationGroups.PrivateLFSUpdated;
            yield return new Tuple<SerializedNotification, string>(new LFSListUpdated
                    { Type = entityState.ToChangeType(), Item = GetInfo() },
                listGroup);

            // TODO: send per-item group, DTO based notification
        }
    }
}
