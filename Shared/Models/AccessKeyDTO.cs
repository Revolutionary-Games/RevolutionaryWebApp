namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class AccessKeyDTO : ClientSideTimedModel
    {
        public string Description { get; set; }
        public DateTime LastUsed { get; set; }
        public int KeyType { get; set; }
    }
}
