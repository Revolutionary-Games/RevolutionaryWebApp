namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;

    [Index(nameof(Email), IsUnique = true)]
    [Index(nameof(ApiToken), IsUnique = true)]
    [Index(nameof(LfsToken), IsUnique = true)]
    [Index(nameof(LauncherLinkCode), IsUnique = true)]
    public class User : IdentityUser<long>, ITimestampedModel
    {
        public bool Local { get; set; }
        public string SsoSource { get; set; }
        public string PasswordDigest { get; set; }

        // TODO: combine these to a single enum field
        public bool? Developer { get; set; } = false;
        public bool? Admin { get; set; } = false;

        public string ApiToken { get; set; }
        public string LfsToken { get; set; }

        public bool? Suspended { get; set; } = false;
        public string SuspendedReason { get; set; }
        public bool? SuspendedManually { get; set; } = false;

        public string LauncherLinkCode { get; set; }
        public DateTime? LauncherCodeExpires { get; set; }

        public int TotalLauncherLinks { get; set; } = 0;

        // Need to reimplement these, as we inherit IdentityUser
        public DateTime CreatedAt { get; set; } = DateTime.Now.ToUniversalTime();
        public DateTime UpdatedAt { get; set; } = DateTime.Now.ToUniversalTime();

        /// <summary>
        ///   Builds verified by this user
        /// </summary>
        public virtual ICollection<DevBuild> DevBuilds { get; set; } = new HashSet<DevBuild>();

        /// <summary>
        ///   Launchers linked to this user
        /// </summary>
        public virtual ICollection<LauncherLink> LauncherLinks { get; set; } = new HashSet<LauncherLink>();

        /// <summary>
        ///   Stored files owned by this user
        /// </summary>
        public virtual ICollection<StorageItem> StorageItems { get; set; } = new HashSet<StorageItem>();

        public virtual ICollection<IdentityUserRole<long>> UserRoles { get; set; }
    }
}
