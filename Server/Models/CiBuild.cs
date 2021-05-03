namespace ThriveDevCenter.Server.Models
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class CiBuild
    {
        public long CiProjectId { get; set; }

        public long CiBuildId { get; set; }

        /// <summary>
        ///   The hash of the commit triggering this build. The build can also contain other commits
        /// </summary>
        [Required]
        public string CommitHash { get; set; }

        /// <summary>
        ///   Reference to the remote ref we need to checkout to run this build
        /// </summary>
        [Required]
        public string RemoteRef { get; set; }

        public CiProject CiProject { get; set; }

        public ICollection<CiJob> CiJobs { get; set; } = new HashSet<CiJob>();
    }
}
