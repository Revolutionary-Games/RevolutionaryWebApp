namespace ThriveDevCenter.Server.Models
{
    using System;
    using Shared;
    using Shared.Models;

    public class UpdateableModel : BaseModel, ITimestampedModel
    {
        [AllowSortingBy]
        public DateTime CreatedAt { get; set; } = DateTime.Now.ToUniversalTime();

        [AllowSortingBy]
        public DateTime UpdatedAt { get; set; } = DateTime.Now.ToUniversalTime();
    }
}
