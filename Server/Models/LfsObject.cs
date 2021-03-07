using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;

    [Index(new []{nameof(LfsProjectId)})]
    public class LfsObject : UpdateableModel
    {
        [Required]
        public string Oid { get; set; }

        public int Size { get; set; }

        [Required]
        public string StoragePath { get; set; }

        public long LfsProjectId { get; set; }
        public LfsProject LfsProject { get; set; }
    }
}
