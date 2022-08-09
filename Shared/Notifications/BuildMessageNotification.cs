namespace ThriveDevCenter.Shared.Notifications;

using System.ComponentModel.DataAnnotations;
using Models;

public class BuildMessageNotification : SerializedNotification
{
    [Required]
    public RealTimeBuildMessage Message { get; set; } = new();
}