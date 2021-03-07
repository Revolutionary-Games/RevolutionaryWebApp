namespace ThriveDevCenter.Shared.Models
{
    using System;

    public interface ITimestampedModel : IIdentifiable
    {
        DateTime CreatedAt { get; }

        DateTime UpdatedAt { get; }
    }
}
