namespace RevolutionaryWebApp.Server.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;

[Index(nameof(LfsProjectId), nameof(Path), nameof(Name), IsUnique = true)]
public class ProjectGitFile : ModelWithCreationTime
{
    [Required]
    [AllowSortingBy]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Path { get; set; } = string.Empty;

    [AllowSortingBy]
    public int? Size { get; set; } = -1;

    [AllowSortingBy]
    public FileType FType { get; set; } = FileType.File;

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
            FType = FType,
            UsesLfsOid = !string.IsNullOrEmpty(LfsOid),
        };
    }
}
