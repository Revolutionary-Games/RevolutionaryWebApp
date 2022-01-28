namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class InProgressClaSignatureDTO : ClientSideTimedModel
    {
        public long ClaId { get; set; }
        public string? Email { get; set; }
        public bool EmailVerified { get; set; }
        public string? GithubAccount { get; set; }
        public long? GithubUserId { get; set; }
        public string? GithubEmail { get; set; }
        public bool GithubSkipped { get; set; }

        [StringLength(AppInfo.PersonsNameMaximumLength, MinimumLength = 1)]
        public string? DeveloperUsername { get; set; }

        [StringLength(AppInfo.PersonsNameMaximumLength, MinimumLength = 1)]
        public string? SignerName { get; set; }

        [StringLength(AppInfo.PersonsNameMaximumLength, MinimumLength = 1)]
        public string? SignerSignature { get; set; }

        public bool? SignerIsMinor { get; set; }

        [StringLength(AppInfo.PersonsNameMaximumLength, MinimumLength = 1)]
        public string? GuardianName { get; set; }

        [StringLength(AppInfo.PersonsNameMaximumLength, MinimumLength = 1)]
        public string? GuardianSignature { get; set; }
    }
}
