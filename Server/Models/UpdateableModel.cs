namespace ThriveDevCenter.Server.Models
{
    using System;
    using Shared;
    using Shared.Models;

    public class UpdateableModel : BaseModel, ITimestampedModel
    {
        [AllowSortingBy]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [AllowSortingBy]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
