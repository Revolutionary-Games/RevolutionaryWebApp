namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Enums;

    public class MeetingDTO : ClientSideTimedModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Minutes { get; set; }

        [Required]
        public string Description { get; set; } = string.Empty;

        public DateTime StartsAt { get; set; }
        public TimeSpan? ExpectedDuration { get; set; }
        public TimeSpan JoinGracePeriod { get; set; }
        public DateTime? EndedAt { get; set; }
        public AssociationResourceAccess ReadAccess { get; set; }
        public AssociationResourceAccess JoinAccess { get; set; }
        public bool ReadOnly { get; set; }
        public long? OwnerId { get; set; }
        public long? SecretaryId { get; set; }
    }
}
