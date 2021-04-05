namespace ThriveDevCenter.Shared.Models
{
    public class ProjectGitFileDTO : ClientSideModel
    {
        public string Name { get; set; }

        public int? Size { get; set; }

        public string Ftype { get; set; }

        public string LfsOid { get; set; }
    }
}
