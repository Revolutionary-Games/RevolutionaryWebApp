namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class BackupDTO : ClientSideTimedModel
{
    [Required]
    public string Name { get; set; } = null!;
    public long Size { get; set; }
}
