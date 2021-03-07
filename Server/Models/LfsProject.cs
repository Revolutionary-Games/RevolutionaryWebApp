using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;

    [Index(nameof(Name), IsUnique = true)]
    [Index(nameof(Slug), IsUnique = true)]
    public class LfsProject : UpdateableModel
    {
        public string Name { get; set; }

        public string Slug { get; set; }

        public bool Public { get; set; } = true;

        [Required]
        public string RepoUrl { get; set; }

        public string CloneUrl { get; set; }
        public int? TotalObjectSize { get; set; }
        public int? TotalObjectCount { get; set; }
        public DateTime? TotalSizeUpdated { get; set; }
        public DateTime? FileTreeUpdated { get; set; }
        public string FileTreeCommit { get; set; }

        public virtual ICollection<LfsObject> LfsObjects { get; set; } = new HashSet<LfsObject>();
        public virtual ICollection<ProjectGitFile> ProjectGitFiles { get; set; } = new HashSet<ProjectGitFile>();
    }
}
