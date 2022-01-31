namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class PatronDTO : ClientSideTimedModel
    {
        [Required]
        public string Email { get; set; } = string.Empty;

        public string? EmailAlias { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        public int PledgeAmountCents { get; set; }

        [Required]
        public string RewardId { get; set; } = string.Empty;

        public bool HasForumAccount { get; set; }
        public bool Suspended { get; set; }
    }
}
