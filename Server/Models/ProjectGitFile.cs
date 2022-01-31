namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;

    [Index(nameof(LfsProjectId), nameof(Name), nameof(Path), IsUnique = true)]
    public class ProjectGitFile : UpdateableModel
    {
        [Required]
        [AllowSortingBy]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Path { get; set; } = string.Empty;

        [AllowSortingBy]
        public int? Size { get; set; } = -1;

        // TODO: switch to an enum. There's already FileType
        [AllowSortingBy]
        public string Ftype { get; set; } = string.Empty;

        public string? LfsOid { get; set; }

        public long LfsProjectId { get; set; }
        public virtual LfsProject? LfsProject { get; set; }

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
