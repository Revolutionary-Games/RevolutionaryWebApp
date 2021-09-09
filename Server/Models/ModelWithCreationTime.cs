namespace ThriveDevCenter.Server.Models
{
    using System;
    using Shared;

    public class ModelWithCreationTime : BaseModel
    {
        [AllowSortingBy]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
