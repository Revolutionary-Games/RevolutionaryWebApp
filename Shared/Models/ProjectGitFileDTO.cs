namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class ProjectGitFileDTO : ClientSideModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public int Size { get; set; }

        [Required]
        public FileType FType { get; set; }

        public bool UsesLfsOid { get; set; }
    }
}
