namespace ThriveDevCenter.Server.Models
{
    /// <summary>
    ///   Tracks who has voted on a poll
    /// </summary>
    public class MeetingPollVotingRecord
    {
        public long MeetingId { get; set; }
        public long PollId { get; set; }
        public long UserId { get; set; }

        public Meeting Meeting { get; set; }
        public MeetingPoll Poll { get; set; }
        public User User { get; set; }
    }
}
