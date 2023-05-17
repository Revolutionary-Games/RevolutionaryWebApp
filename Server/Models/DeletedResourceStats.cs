namespace ThriveDevCenter.Server.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
///   Database record that stores info about other deleted database things
/// </summary>
public class DeletedResourceStats
{
    public DeletedResourceStats(ResourceType type)
    {
        Type = type;
    }

    public enum ResourceType
    {
        DevBuild,
    }

    [Key]
    public ResourceType Type { get; private set; }

    public long ItemCount { get; set; }

    public long ItemsExtraAttribute { get; set; }
}
