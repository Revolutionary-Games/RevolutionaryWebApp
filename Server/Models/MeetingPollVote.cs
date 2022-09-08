namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using SharedBase.Utilities;

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
    ///   If true (this is the president's vote) this is used to select which option (in non-election votes) is
    ///   used as tiebreak winner
    /// </summary>
    public bool IsTiebreaker { get; set; }

    /// <summary>
    ///   Vote data encoded as JSON
    /// </summary>
    [Required]
    public string VoteContent { get; set; } = string.Empty;

    public Meeting? Meeting { get; set; }
    public MeetingPoll? Poll { get; set; }

    [NotMapped]
    public PollVoteData ParsedVoteContent
    {
        get => JsonSerializer.Deserialize<PollVoteData>(VoteContent) ?? throw new NullDecodedJsonException();
        set
        {
            VoteContent = JsonSerializer.Serialize(value);
        }
    }
}
