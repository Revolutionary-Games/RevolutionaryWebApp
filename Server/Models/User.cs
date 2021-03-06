namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;

    [Index(nameof(Email), IsUnique = true)]
    [Index(nameof(ApiToken), IsUnique = true)]
    [Index(nameof(LfsToken), IsUnique = true)]
    [Index(nameof(LauncherLinkCode), IsUnique = true)]
    public class User : UpdateableModel
    {
        [Required]
        public string Email { get; set; }

        [Required]
        public string Name { get; set; }

        public bool Local { get; set; }
        public string SsoSource { get; set; }
        public string PasswordDigest { get; set; }

        public bool Developer { get; set; }
        public bool Admin { get; set; }

        public string ApiToken { get; set; }
        public string LfsToken { get; set; }

        public bool? Suspended { get; set; } = false;
        public string SuspendedReason { get; set; }
        public bool? SuspendedManually { get; set; } = false;

        public string LauncherLinkCode { get; set; }
        public DateTime LauncherCodeExpires { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int TotalLauncherLinks { get; set; } = 0;

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SessionVersion { get; set; } = 0;
    }
}
