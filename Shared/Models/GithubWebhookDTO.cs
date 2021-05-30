namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class GithubWebhookDTO : ClientSideTimedModel
    {
        public string Secret { get; set; }

        public DateTime? LastUsed { get; set; }
    }
}
