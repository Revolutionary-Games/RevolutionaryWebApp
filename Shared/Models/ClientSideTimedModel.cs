namespace ThriveDevCenter.Shared.Models
{
    using System;

    public abstract class ClientSideTimedModel : ClientSideModel, ITimestampedModel
    {
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
