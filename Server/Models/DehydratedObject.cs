namespace ThriveDevCenter.Server.Models
{
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;

    // TODO: drop the update info from this table as unnecessary in a later migration
    [Index(nameof(Sha3), IsUnique=true)]
    [Index(nameof(StorageItemId))]
    public class DehydratedObject : UpdateableModel
    {
        [Required]
        public string Sha3 { get; set; }

        public long StorageItemId { get; set; }

        public StorageItem StorageItem { get; set; }

        /// <summary>
        ///   DevBuilds that contain this object
        /// </summary>
        public ICollection<DevBuild> DevBuilds { get; set; } = new HashSet<DevBuild>();
    }
}
