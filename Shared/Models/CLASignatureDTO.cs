namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;

    public class CLASignatureDTO : ClientSideModel
    {
        public DateTime CreatedAt { get; set; }
        public DateTime? ValidUntil { get; set; }

        [Required]
        public string Email { get; set; } = string.Empty;

        public string? GithubAccount { get; set; }
        public long? GithubUserId { get; set; }
        public string? DeveloperUsername { get; set; }
        public long ClaId { get; set; }
        public long? UserId { get; set; }
    }
}
