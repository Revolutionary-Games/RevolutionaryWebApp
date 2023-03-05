namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(OperationType))]
public class ExecutedMaintenanceOperation : ModelWithCreationTime, IUpdateNotifications
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

    public ExecutedMaintenanceOperationDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            OperationType = OperationType,
            ExtendedDescription = ExtendedDescription,
            FinishedAt = FinishedAt,
            PerformedById = PerformedById,
            Failed = Failed,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new ExecutedMaintenanceOperationListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
        }, NotificationGroups.UserListUpdated);
    }
}
