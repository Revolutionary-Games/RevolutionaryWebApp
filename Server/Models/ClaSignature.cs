namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;

    [Index(nameof(ClaId), nameof(Email))]
    [Index(nameof(ClaId), nameof(GithubAccount))]
    [Index(nameof(ClaSignatureStoragePath), IsUnique = true)]
    [Index(nameof(ClaInvalidationStoragePath), IsUnique = true)]
    public class ClaSignature : BaseModel
    {
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ValidUntil { get; set; }

        [Required]
        public string Email { get; set; }

        public string GithubAccount { get; set; }

        public string DeveloperUsername { get; set; }

        [Required]
        public string ClaSignatureStoragePath { get; set; }

        public string ClaInvalidationStoragePath { get; set; }

        public long ClaId { get; set; }

        public long? UserId { get; set; }

        public Cla Cla { get; set; }

        public User User { get; set; }
    }
}
