namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class CLASignatureDTO : ClientSideModel
    {
        public DateTime CreatedAt { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string Email { get; set; }
        public string GithubAccount { get; set; }
        public long? GithubUserId { get; set; }
        public string DeveloperUsername { get; set; }
        public long ClaId { get; set; }
        public long? UserId { get; set; }
    }
}
