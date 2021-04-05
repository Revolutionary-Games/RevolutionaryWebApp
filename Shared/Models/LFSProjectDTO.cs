namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class LFSProjectDTO : ClientSideTimedModel
    {
        public string Name { get; set; }
        public string Slug { get; set; }
        public bool Public { get; set; }
        public int TotalObjectSize { get; set; }
        public int TotalObjectCount { get; set; }
        public DateTime? TotalSizeUpdated { get; set; }
        public DateTime? FileTreeUpdated { get; set; }
        public string FileTreeCommit { get; set; }
        public string RepoUrl { get; set; }
        public string CloneUrl { get; set; }
        public string LfsBaseUrl { get; set; }
    }
}
