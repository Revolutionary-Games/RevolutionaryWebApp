namespace ThriveDevCenter.Shared.Models
{
    using System;

    public abstract class ClientSideTimedModel : ITimestampedModel
    {
        public long Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
