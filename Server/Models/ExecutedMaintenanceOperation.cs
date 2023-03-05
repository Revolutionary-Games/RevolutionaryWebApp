namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;

[Index(nameof(OperationType))]
public class ExecutedMaintenanceOperation : ModelWithCreationTime
{
    public ExecutedMaintenanceOperation(string operationType)
    {
        OperationType = operationType;
    }

    [Required]
    [AllowSortingBy]
    public string OperationType { get; set; }

    public string? ExtendedDescription { get; set; }

    [AllowSortingBy]
    public DateTime? FinishedAt { get; set; }

    [AllowSortingBy]
    public long? PerformedById { get; set; }

    public User? PerformedBy { get; set; }

    /// <summary>
    ///   True when this has failed and an admin should look into the job or server logs to see the problem
    /// </summary>
    public bool Failed { get; set; }
}
