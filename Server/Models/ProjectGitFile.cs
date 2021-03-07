using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using Microsoft.EntityFrameworkCore;

    [Index(new []{nameof(Path), nameof(Name), nameof(LfsProjectId)}, IsUnique = true)]
    public class ProjectGitFile : UpdateableModel
    {
        public string Name { get; set; }

        public string Path { get; set; }

        public int? Size { get; set; } = -1;

        // TODO: switch to an enum
        public string Ftype { get; set; }

        public string LfsOid { get; set; }

        public long LfsProjectId { get; set; }
        public virtual LfsProject LfsProject { get; set; }
    }
}
