namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class LFSProjectInfo
    {
        public long ID { get; set; }

        public string Name { get; set; }

        public bool Public { get; set; }

        public long Size { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
