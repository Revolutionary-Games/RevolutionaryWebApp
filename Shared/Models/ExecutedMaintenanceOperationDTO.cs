namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class ExecutedMaintenanceOperationDTO : ClientSideModelWithCreationTime
{
    [Required]
    public string OperationType { get; set; } = null!;

    public string? ExtendedDescription { get; set; }
    public DateTime? FinishedAt { get; set; }
    public long? PerformedById { get; set; }
    public bool Failed { get; set; }
}
