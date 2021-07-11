namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Text.Json;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;
    using Shared.Models.Enums;
    using Shared.Notifications;
    using Utilities;

    /// <summary>
    ///   A poll held during a meeting
    /// </summary>
    [Index(nameof(MeetingId), nameof(Title), IsUnique = true)]
    public class MeetingPoll : IUpdateNotifications
    {
        public long MeetingId { get; set; }

        public long PollId { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public VotingTiebreakType TiebreakType { get; set; }

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

        [NotMapped]
        public PollData ParsedData
        {
            get => JsonSerializer.Deserialize<PollData>(PollData);
            set
            {
                PollData = JsonSerializer.Serialize(value);
            }
        }

        [NotMapped]
        public PollResultData ParsedResults
        {
            get => JsonSerializer.Deserialize<PollResultData>(PollResults);
            set
            {
                PollResults = JsonSerializer.Serialize(value);
            }
        }

        public MeetingPollDTO GetDTO()
        {
            return new()
            {
                MeetingId = MeetingId,
                PollId = PollId,
                Title = Title,
                TiebreakType = TiebreakType,
                PollData = PollData,
                PollResults = PollResults,
                PollResultsCreatedAt = PollResultsCreatedAt,
                CreatedAt = CreatedAt,
                ClosedAt = ClosedAt,
                AutoCloseAt = AutoCloseAt,
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            yield return new Tuple<SerializedNotification, string>(new MeetingPollListUpdated()
                    { Type = entityState.ToChangeType(), Item = GetDTO() },
                NotificationGroups.MeetingPollListUpdatedPrefix + MeetingId);
        }
    }
}
