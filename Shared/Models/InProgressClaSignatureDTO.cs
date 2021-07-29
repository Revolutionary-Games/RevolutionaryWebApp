namespace ThriveDevCenter.Shared.Models
{
    public class InProgressClaSignatureDTO : ClientSideTimedModel
    {
        public string Email { get; set; }
        public bool EmailVerified { get; set; }
        public string GithubAccount { get; set; }
        public bool GithubSkipped { get; set; }
        public string DeveloperUsername { get; set; }
        public string SignerName { get; set; }
        public string SignerSignature { get; set; }
        public bool? SignerIsMinor { get; set; }
        public string GuardianName { get; set; }
        public string GuardianSignature { get; set; }
    }
}
