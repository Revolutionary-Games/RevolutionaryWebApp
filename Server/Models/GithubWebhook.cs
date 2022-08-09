namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Authorization;
    using Shared.Models;
    using Utilities;

    public class GithubWebhook : UpdateableModel, IContainsHashedLookUps
    {
        [HashedLookUp]
        [Required]
        public string Secret { get; set; } = string.Empty;

        [Required]
        public string HashedSecret { get; set; } = string.Empty;

        public DateTime? LastUsed { get; set; }

        public GithubWebhookDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Secret = Secret,
                LastUsed = LastUsed,
            };
        }

        public void CreateSecret()
        {
            Secret = NonceGenerator.GenerateNonce(32);
            this.BumpUpdatedAt();
        }
    }
}
