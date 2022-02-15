namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;

    public class LFSProjectDTO : ClientSideTimedModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Slug { get; set; } = string.Empty;

        public bool Public { get; set; }
        public long TotalObjectSize { get; set; }
        public int TotalObjectCount { get; set; }
        public DateTime? TotalSizeUpdated { get; set; }
        public DateTime? FileTreeUpdated { get; set; }
        public string? FileTreeCommit { get; set; }

        [Required]
        public string RepoUrl { get; set; } = string.Empty;

        [Required]
        public string CloneUrl { get; set; } = string.Empty;

        [Required]
        public string LfsUrlSuffix { get; set; } = string.Empty;

        /// <summary>
        ///   Admins can view deleted items to restore them
        /// </summary>
        public bool Deleted { get; set; }

        [Required]
        public string BranchToBuildFileTreeFor { get; set; } = string.Empty;
    }
}
