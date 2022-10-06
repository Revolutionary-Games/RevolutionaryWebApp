namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class CIProjectDTO : ClientSideTimedModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public bool Public { get; set; }
    public bool Deleted { get; set; }
    public bool Enabled { get; set; } = true;

    [Required]
    [MaxLength(250)]
    public string RepositoryCloneUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string RepositoryFullName { get; set; } = string.Empty;

    public CIProjectType ProjectType { get; set; }

    [Required]
    public string DefaultBranch { get; set; } = "master";
}
