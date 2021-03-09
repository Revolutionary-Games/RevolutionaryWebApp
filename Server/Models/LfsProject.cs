using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.Data.Common;
    using System.Drawing;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;

    [Index(nameof(Name), IsUnique = true)]
    [Index(nameof(Slug), IsUnique = true)]
    public class LfsProject : UpdateableModel
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
    }
}
