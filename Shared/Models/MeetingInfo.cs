namespace ThriveDevCenter.Shared.Models
{
    using System;
    using Enums;

    public class MeetingInfo : ClientSideTimedModel
    {
        public string Name { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public AssociationResourceAccess ReadAccess { get; set; }
        public bool ReadOnly { get; set; }
    }
}
