namespace ThriveDevCenter.Server.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using Shared.Models;

    public class CiJob
    {
        public long CiProjectId { get; set; }

        public long CiBuildId { get; set; }

        public long CiJobId { get; set; }

        public CIJobState State { get; set; } = CIJobState.Starting;

        [Required]
        public string JobName { get; set; }

        [ForeignKey("CiProjectId,CiBuildId")]
        public CiBuild Build { get; set; }

        public ICollection<CiJobArtifact> CiJobArtifacts { get; set; } = new HashSet<CiJobArtifact>();
    }
}
