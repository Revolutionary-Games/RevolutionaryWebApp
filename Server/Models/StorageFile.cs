using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;

    [Index(nameof(StoragePath), IsUnique = true)]
    [Index(nameof(Uploading))]
    public class StorageFile : UpdateableModel
    {
        [Required]
        public string StoragePath { get; set; }

        public int? Size { get; set; }

        public bool AllowParentless { get; set; } = false;

        public bool Uploading { get; set; } = true;
        public DateTime? UploadExpires { get; set; }

        public virtual ICollection<StorageItemVersion> StorageItemVersions { get; set; } =
            new HashSet<StorageItemVersion>();
    }
}
