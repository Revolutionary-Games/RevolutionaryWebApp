namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class CLADTO : ClientSideModel
    {
        public DateTime CreatedAt { get; set; }
        public bool Active { get; set; }
        public string RawMarkdown { get; set; }
    }
}
