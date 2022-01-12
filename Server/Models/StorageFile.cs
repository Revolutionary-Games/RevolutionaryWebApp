using System;
using System.Collections.Generic;

namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using Microsoft.EntityFrameworkCore;

    [Index(nameof(StoragePath), IsUnique = true)]
    [Index(nameof(Uploading))]
    public class StorageFile : UpdateableModel
    {
        [Required]
        public string StoragePath { get; set; }

        // TODO: make non-nullable
        public long? Size { get; set; }

        public bool AllowParentless { get; set; } = false;

        public bool Uploading { get; set; } = true;
        public DateTime? UploadExpires { get; set; }

        public ICollection<StorageItemVersion> StorageItemVersions { get; set; } =
            new HashSet<StorageItemVersion>();

        /// <summary>
        ///   The path to allow clients to upload this files to.
        ///   This is separate from final path to not allow potential attacks where the client uses the still valid put
        ///   URL to send a new version after the server thinks uploading is finished. This uses '@' prefix as
        ///   folder names etc. are directly but in the remote storage bucket, so there might be some attack possible
        ///   as such this prefix is used (but is not used for LFS as there the client can't control the file names
        ///   as much).
        /// </summary>
        [NotMapped]
        public string UploadPath => "@upload/" + StoragePath;

        public void OnVersionUploadFinished(StorageItemVersion uploadedVersion)
        {
            uploadedVersion.Uploading = false;

            Uploading = false;
        }
    }
}
