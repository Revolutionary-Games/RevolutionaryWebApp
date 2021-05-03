namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class CIProjectDTO : ClientSideTimedModel
    {
        [Required]
        public string Name { get; set; }
        public bool Public { get; set; }
        public bool Deleted { get; set; }
        public bool Enabled { get; set; } = true;

        [Required]
        [MaxLength(250)]
        public string RepositoryCloneUrl { get; set; }

        [Required]
        [MaxLength(150)]
        public string RepositoryFullName { get; set; }

        public CIProjectType ProjectType { get; set; }

        [Required]
        public string DefaultBranch { get; set; } = "master";
    }
}
