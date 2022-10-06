namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;
using Enums;

public class CISecretDTO : IIdentifiable
{
    public long CiProjectId { get; set; }
    public long CiSecretId { get; set; }
    public CISecretType UsedForBuildTypes { get; set; }

    [Required]
    public string SecretName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///   Used for notifications to detect which model was updated, that's why this shouldn't be super bad that
    ///   we generate a fake ID like this
    /// </summary>
    [JsonIgnore]
    public long Id => (CiSecretId << 32) + CiProjectId;
}
