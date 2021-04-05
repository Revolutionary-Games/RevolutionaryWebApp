namespace ThriveDevCenter.Server.Models
{
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;

    [Index(new[] { nameof(LfsProjectId), nameof(Name), nameof(Path) }, IsUnique = true)]
    public class ProjectGitFile : UpdateableModel
    {
        [AllowSortingBy]
        public string Name { get; set; }

        public string Path { get; set; }

        [AllowSortingBy]
        public int? Size { get; set; } = -1;

        // TODO: switch to an enum
        [AllowSortingBy]
        public string Ftype { get; set; }

        public string LfsOid { get; set; }

        public long LfsProjectId { get; set; }
        public virtual LfsProject LfsProject { get; set; }

        public ProjectGitFileDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                Name = Name,
                Size = Size ?? 0,
                Ftype = Ftype,
                UsesLfsOid = !string.IsNullOrEmpty(LfsOid)
            };
        }
    }
}
