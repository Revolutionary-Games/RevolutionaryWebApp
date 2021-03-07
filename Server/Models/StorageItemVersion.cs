using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using Microsoft.EntityFrameworkCore;

    [Index(nameof(StorageFileId))]
    [Index(new[] { nameof(StorageItemId), nameof(Version) }, IsUnique = true)]
    public class StorageItemVersion : UpdateableModel
    {
        public int Version { get; set; } = 1;

        public long StorageItemId { get; set; }
        public StorageItem StorageItem { get; set; }

        public long StorageFileId { get; set; }
        public StorageFile StorageFile { get; set; }

        public bool Keep { get; set; } = false;
        public bool Protected { get; set; } = false;
        public bool Uploading { get; set; } = true;
    }
}
