namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Shared;

    public class EmailTokenData
    {
        [Required]
        [StringLength(AppInfo.MaxEmailLength, MinimumLength = 3)]
        public string SentToEmail { get; set; }

        [Required]
        public EmailVerificationType Type { get; set; }

        [Required]
        [StringLength(1024, MinimumLength = 3)]
        public string VerifiedResourceId { get; set; }
    }

    public enum EmailVerificationType
    {
        CLA,
    }
}
