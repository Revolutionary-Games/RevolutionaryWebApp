namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.Net;

    public class AccessKeyDTO : ClientSideTimedModel
    {
        public string Description { get; set; }
        public DateTime? LastUsed { get; set; }
        public AccessKeyType KeyType { get; set; }
        public IPAddress LastUsedFrom { get; set; }
    }
}
