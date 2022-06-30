namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Converters;
    using Enums;

    public class MeetingPollDTO : IIdentifiable
    {
        public long MeetingId { get; set; }
        public long PollId { get; set; }

        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Title { get; set; } = string.Empty;

        public VotingTiebreakType TiebreakType { get; set; }

        [Required]
        public string PollData { get; set; } = string.Empty;

        public string? PollResults { get; set; }
        public DateTime? PollResultsCreatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public DateTime? AutoCloseAt { get; set; }
        public long? ManuallyClosedById { get; set; }

        [JsonIgnore]
        public long Id => (PollId << 48) + MeetingId;

        [JsonIgnore]
        public PollData ParsedData
        {
            get
            {
                return JsonSerializer.Deserialize<PollData>(PollData) ?? throw new NullDecodedJsonException();
            }
            set
            {
                PollData = JsonSerializer.Serialize(value);
            }
        }

        [JsonIgnore]
        public PollResultData? ParsedResults
        {
            get
            {
                if (PollResults == null)
                    return null;

                return JsonSerializer.Deserialize<PollResultData>(PollResults);
            }
            set
            {
                PollResults = JsonSerializer.Serialize(value);
            }
        }
    }
}
