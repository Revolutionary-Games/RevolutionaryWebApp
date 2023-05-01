namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class AvailableMaintenanceOperation
{
    public AvailableMaintenanceOperation(string name, string extraDescription)
    {
        Name = name;
        ExtraDescription = extraDescription;
    }

    [Required]
    public string Name { get; }

    [Required]
    public string ExtraDescription { get; }
}
