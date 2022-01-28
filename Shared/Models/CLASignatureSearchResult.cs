namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class CLASignatureSearchResult
    {
        public string? Email { get; set; }
        public string? GithubAccount { get; set; }
        public string? DeveloperUsername { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
