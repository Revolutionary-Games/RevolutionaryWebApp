namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using Microsoft.EntityFrameworkCore;

    /// <summary>
    ///   A poll held during a meeting
    /// </summary>
    [Index(nameof(MeetingId), nameof(Title), IsUnique = true)]
    public class MeetingPoll
    {
        public long MeetingId { get; set; }

        public long PollId { get; set; }

        [Required]
        public string Title { get; set; }

        /// <summary>
        ///   Poll data encoded as JSON
        /// </summary>
        [Required]
        public string PollData { get; set; }

        /// <summary>
        ///   Poll results encoded as JSON
        /// </summary>
        public string PollResults { get; set; }

        public DateTime? PollResultsCreatedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ClosedAt { get; set; }

        public DateTime? AutoCloseAt { get; set; }

        public Meeting Meeting { get; set; }

        public ICollection<MeetingPollVotingRecord> VotingRecords { get; set; } =
            new HashSet<MeetingPollVotingRecord>();

        public ICollection<MeetingPollVote> Votes { get; set; } = new HashSet<MeetingPollVote>();

        [NotMapped]
        public bool Open => ClosedAt == null;
    }
}
