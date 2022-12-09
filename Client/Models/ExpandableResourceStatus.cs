namespace ThriveDevCenter.Client.Models;

public class ExpandableResourceStatus : DeletedResourceStatus
{
    /// <summary>
    ///   True when the resource should be shown in an expanded view
    /// </summary>
    public bool Expanded { get; set; }
}
