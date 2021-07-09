namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;

    /// <summary>
    ///   Separately stored cast vote on a poll. This is stored separately to add anonymity
    /// </summary>
    [Index(nameof(MeetingId), nameof(PollId))]
    public class MeetingPollVote
    {
        [Key]
        public Guid VoteId { get; set; } = Guid.NewGuid();

        public long MeetingId { get; set; }
        public long PollId { get; set; }

        /// <summary>
        ///   Some people have higher voting power so that is stored here, Most common values are 1 and 2.
        /// </summary>
        public float VotingPower { get; set; } = 1;

        /// <summary>
        ///   Vote data encoded as JSON
        /// </summary>
        [Required]
        public string VoteContent { get; set; }

        public Meeting Meeting { get; set; }
        public MeetingPoll Poll { get; set; }
    }
}
