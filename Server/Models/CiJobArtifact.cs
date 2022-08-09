namespace ThriveDevCenter.Server.Models;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class CiJobArtifact
{
    public long CiProjectId { get; set; }

    public long CiBuildId { get; set; }

    public long CiJobId { get; set; }

    public long CiJobArtifactId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public long StorageItemId { get; set; }

    public StorageItem? StorageItem { get; set; }

    [ForeignKey("CiProjectId,CiBuildId,CiJobId")]
    public CiJob? Job { get; set; }
}