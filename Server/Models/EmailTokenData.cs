namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Shared;

    public class EmailTokenData
    {
        [Required]
        [StringLength(AppInfo.MaxEmailLength, MinimumLength = 3)]
        public string SentToEmail { get; set; } = string.Empty;

        [Required]
        public EmailVerificationType Type { get; set; }

        [Required]
        [StringLength(1024, MinimumLength = 3)]
        public string VerifiedResourceId { get; set; } = string.Empty;
    }

    public enum EmailVerificationType
    {
        CLA,
    }
}
