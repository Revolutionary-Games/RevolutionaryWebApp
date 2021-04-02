using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using Microsoft.EntityFrameworkCore;

    [Index(new[] { nameof(Name), nameof(ParentId) }, IsUnique = true)]
    [Index(nameof(AllowParentless))]
    [Index(nameof(OwnerId))]

    // TODO: is this a duplicate index that is not needed?
    [Index(nameof(ParentId))]
    public class StorageItem : UpdateableModel, IOwneableModel
    {
        // TODO: is there a threat from timing attack trying to enumerate existing files?
        public string Name { get; set; }

        // TODO: move to enum
        public int? Ftype { get; set; }
        public bool Special { get; set; } = false;

        public int? Size { get; set; }

        // TODO: change these two to enums as well
        public int? ReadAccess { get; set; }
        public int? WriteAccess { get; set; }

        public long? OwnerId { get; set; }
        public User Owner { get; set; }

        public long? ParentId { get; set; }
        public StorageItem Parent { get; set; }
        public bool AllowParentless { get; set; } = false;

        // TODO: can this be named something else? This is the children of this item
        public ICollection<StorageItem> Children { get; set; } = new HashSet<StorageItem>();
        public ICollection<StorageItemVersion> StorageItemVersions { get; set; } = new HashSet<StorageItemVersion>();

        // Things that can reference this
        public ICollection<DehydratedObject> DehydratedObjects { get; set; } = new HashSet<DehydratedObject>();
        public ICollection<DevBuild> DevBuilds { get; set; } = new HashSet<DevBuild>();
    }
}
