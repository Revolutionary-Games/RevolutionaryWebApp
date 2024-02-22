namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class PrecompiledObjectInfo : ClientSideTimedModel
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Name { get; set; } = null!;

    public long TotalStorageSize { get; set; }

    public bool Public { get; set; } = true;
}
