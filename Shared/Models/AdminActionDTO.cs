namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class AdminActionDTO : ClientSideModel
{
    [Required]
    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public long? TargetUserId { get; set; }
    public long? PerformedById { get; set; }
}
