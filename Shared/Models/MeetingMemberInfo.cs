namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.Text.Json.Serialization;

    public class MeetingMemberInfo : IIdentifiable
    {
        public long MeetingId { get; set; }
        public long UserId { get; set; }
        public DateTime JoinedAt { get; set; }

        [JsonIgnore]
        public long Id => (UserId << 24) + MeetingId;
    }
}
