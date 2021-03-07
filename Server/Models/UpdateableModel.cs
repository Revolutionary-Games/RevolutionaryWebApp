namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;
    using Shared.Models;

    public class UpdateableModel : BaseModel, ITimestampedModel
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now.ToUniversalTime();

        public DateTime UpdatedAt { get; set; } = DateTime.Now.ToUniversalTime();

        public void BumpUpdatedAt()
        {
            UpdatedAt = DateTime.Now.ToUniversalTime();
        }
    }
}
