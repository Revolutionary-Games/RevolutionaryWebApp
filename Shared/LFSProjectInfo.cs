namespace ThriveDevCenter.Shared
{
    using System;

    public class LFSProjectInfo
    {
        public string Name { get; set; }

        public bool Public { get; set; }

        public long Size { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
