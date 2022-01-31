namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Enums;

    public class MeetingInfo : ClientSideTimedModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public DateTime StartsAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public AssociationResourceAccess ReadAccess { get; set; }
        public bool ReadOnly { get; set; }
    }
}
