namespace ThriveDevCenter.Server.Models;

using System;
using Shared;
using Shared.Models;

public class UpdateableModel : ModelWithCreationTime, ITimestampedModel
{
    [AllowSortingBy]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}