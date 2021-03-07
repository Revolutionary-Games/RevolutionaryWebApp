namespace ThriveDevCenter.Shared.Models
{
    using System;

    public interface ITimestampedModel : IIdentifiable
    {
        DateTime CreatedAt { get; }

        DateTime UpdatedAt { get; set; }
    }

    public static class TimestampedModelHelpers
    {
        public static void BumpUpdatedAt(this ITimestampedModel entity)
        {
            entity.UpdatedAt = DateTime.Now.ToUniversalTime();
        }
    }
}
