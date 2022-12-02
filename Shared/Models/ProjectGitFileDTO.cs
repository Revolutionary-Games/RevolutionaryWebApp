namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using DevCenterCommunication.Models.Enums;

public class ProjectGitFileDTO : ClientSideModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public int Size { get; set; }

    [Required]
    public FileType FType { get; set; }

    public bool UsesLfsOid { get; set; }
}
