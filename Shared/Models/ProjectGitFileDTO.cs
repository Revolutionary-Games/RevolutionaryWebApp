namespace ThriveDevCenter.Shared.Models
{
    using System.ComponentModel.DataAnnotations;

    public class ProjectGitFileDTO : ClientSideModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public int Size { get; set; }

        [Required]
        public string Ftype { get; set; } = string.Empty;

        public bool UsesLfsOid { get; set; }
    }
}
