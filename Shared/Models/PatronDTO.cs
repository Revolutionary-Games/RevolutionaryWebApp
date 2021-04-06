namespace ThriveDevCenter.Shared.Models
{
    public class PatronDTO : ClientSideTimedModel
    {
        public string Email { get; set; }
        public string EmailAlias { get; set; }
        public string Username { get; set; }
        public int PledgeAmountCents { get; set; }
        public string RewardId { get; set; }
        public bool HasForumAccount { get; set; }
        public bool Suspended { get; set; }
    }
}
