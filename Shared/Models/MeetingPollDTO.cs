namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Enums;

    public class MeetingPollDTO : IIdentifiable
    {
        public long MeetingId { get; set; }
        public long PollId { get; set; }

        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Title { get; set; }

        public VotingTiebreakType TiebreakType { get; set; }
        public string PollData { get; set; }
        public string PollResults { get; set; }
        public DateTime? PollResultsCreatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public DateTime? AutoCloseAt { get; set; }

        [JsonIgnore]
        public long Id => PollId << 48 & MeetingId;

        [JsonIgnore]
        public PollData ParsedData
        {
            get => JsonSerializer.Deserialize<PollData>(PollData);
            set
            {
                PollData = JsonSerializer.Serialize(value);
            }
        }

        [JsonIgnore]
        public PollResultData ParsedResults
        {
            get => JsonSerializer.Deserialize<PollResultData>(PollResults);
            set
            {
                PollResults = JsonSerializer.Serialize(value);
            }
        }
    }
}
