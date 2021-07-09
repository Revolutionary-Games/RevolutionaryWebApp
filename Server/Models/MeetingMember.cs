namespace ThriveDevCenter.Server.Models
{
    using System;

    public class MeetingMember
    {
        public long MeetingId { get; set; }

        public long UserId { get; set; }

        public DateTime JoinedAt { get; set; }

        public bool CanReviewMinutes { get; set; }

        public Meeting Meeting { get; set; }
        public User User { get; set; }
    }
}
