namespace ThriveDevCenter.Server.Models;

using System;
using DevCenterCommunication.Models;
using Shared;

public class UpdateableModel : ModelWithCreationTime, ITimestampedModel
{
    [AllowSortingBy]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
