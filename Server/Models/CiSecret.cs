namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Common.Models;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using Utilities;

[Index(new[] { nameof(CiProjectId), nameof(SecretName), nameof(UsedForBuildTypes) }, IsUnique = true)]
public class CiSecret : IUpdateNotifications
{
    public long CiProjectId { get; set; }

    public long CiSecretId { get; set; }

    [Required]
    public CISecretType UsedForBuildTypes { get; set; }

    [Required]
    [AllowSortingBy]
    public string SecretName { get; set; } = string.Empty;

    [Required]
    public string SecretContent { get; set; } = string.Empty;

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CiProject? CiProject { get; set; }

    public CISecretDTO GetDTO()
    {
        return new()
        {
            CiProjectId = CiProjectId,
            CiSecretId = CiSecretId,
            UsedForBuildTypes = UsedForBuildTypes,
            SecretName = SecretName,
            CreatedAt = CreatedAt,
        };
    }

    public CiSecretExecutorData ToExecutorData()
    {
        return new()
        {
            SecretName = SecretName,
            SecretContent = SecretContent,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        var dto = GetDTO();

        yield return new Tuple<SerializedNotification, string>(new CIProjectSecretsUpdated()
        {
            Type = entityState.ToChangeType(),
            Item = dto,
        }, NotificationGroups.CIProjectSecretsUpdatedPrefix + CiProjectId);
    }
}