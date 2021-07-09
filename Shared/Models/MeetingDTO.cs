namespace ThriveDevCenter.Shared.Models
{
    using System;
    using Enums;

    public class MeetingDTO : ClientSideTimedModel
    {
        public string Name { get; set; }
        public string Minutes { get; set; }
        public string Description { get; set; }
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
