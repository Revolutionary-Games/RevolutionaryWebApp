namespace ThriveDevCenter.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using Models.Enums;

public class BulkCLACheckRequest
{
    [Required]
    [StringLength(10000, MinimumLength = 3)]
    [Display(Name = "Items To Check")]
    public string ItemsToCheck { get; set; } = string.Empty;

    public CLACheckRequestType CheckType { get; set; }

    /// <summary>
    ///   If true then the not found items are returned, otherwise returns the valid items
    /// </summary>
    public bool ReturnNotFound { get; set; }
}