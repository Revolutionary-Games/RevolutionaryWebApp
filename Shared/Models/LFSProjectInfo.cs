namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class LFSProjectInfo : UpdateableModel
    {
        public string Name { get; set; }

        public bool Public { get; set; }

        public long Size { get; set; }
    }
}
