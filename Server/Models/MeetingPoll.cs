namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared;
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
    [AllowSortingBy]
    public long MeetingId { get; set; }

    [AllowSortingBy]
    public long PollId { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    [Required]
    public VotingTiebreakType TiebreakType { get; set; }

    /// <summary>
    ///   Poll data encoded as JSON
    /// </summary>
    [Required]
    public string PollData { get; set; } = string.Empty;

    /// <summary>
    ///   Poll results encoded as JSON
    /// </summary>
    public string? PollResults { get; set; }

    public DateTime? PollResultsCreatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    public DateTime? AutoCloseAt { get; set; }

    public long? ManuallyClosedById { get; set; }

    public Meeting? Meeting { get; set; }

    public ICollection<MeetingPollVotingRecord> VotingRecords { get; set; } =
        new HashSet<MeetingPollVotingRecord>();

    public ICollection<MeetingPollVote> Votes { get; set; } = new HashSet<MeetingPollVote>();

    public User? ManuallyClosedBy { get; set; }

    [Timestamp]
    public uint Version { get; set; }

    [NotMapped]
    public bool Open => ClosedAt == null;

    [NotMapped]
    public PollData ParsedData
    {
        get => JsonSerializer.Deserialize<PollData>(PollData) ??
            throw new InvalidOperationException("Decoded JSON is null");
        set
        {
            PollData = JsonSerializer.Serialize(value);
        }
    }

    [NotMapped]
    public PollResultData? ParsedResults
    {
        get => PollResults != null ? JsonSerializer.Deserialize<PollResultData>(PollResults) : null;
        set
        {
            if (value == null)
            {
                PollResults = null;
                return;
            }

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
            ManuallyClosedById = ManuallyClosedById,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(
            new MeetingPollListUpdated { Type = entityState.ToChangeType(), Item = GetDTO() },
            NotificationGroups.MeetingPollListUpdatedPrefix + MeetingId);
    }

    /// <summary>
    ///   Calculates the results for this poll and stores it in PollResults
    /// </summary>
    /// <param name="votes">The votes to count</param>
    public void CalculateResults(IEnumerable<MeetingPollVote> votes)
    {
        var settings = ParsedData;

        var countedVotes = new Dictionary<int, double>();

        // Add all potential options to have everything with 0 votes on it as well
        foreach (var choice in settings.Choices)
        {
            countedVotes[choice.Key] = 0;
        }

        MeetingPollVote? tieBreaker;

        if (settings.SingleChoiceOption != null)
        {
            CalculateSimpleVotes(votes, countedVotes, out tieBreaker, true);
        }
        else if (settings.MultipleChoiceOption != null)
        {
            CalculateSimpleVotes(votes, countedVotes, out tieBreaker, false);
        }
        else if (settings.WeightedChoices != null)
        {
            CalculateWeightedVotes(votes, countedVotes, out tieBreaker);
        }
        else
        {
            throw new InvalidOperationException("Unknown poll type to calculate results for");
        }

        var totalVotes = countedVotes.Values.Sum();

        PollResultData results = new()
        {
            Results = countedVotes.AsEnumerable().Select(pair => new PollResultData.ChoiceVotes(pair.Key, pair.Value))
                .OrderByDescending(tuple => tuple.Item2).ToList(),
            TotalVotes = totalVotes,
        };

        const double epsilon = 0.00001;

        if (results.Results.Count > 1 && Math.Abs(results.Results[0].Item2 - results.Results[1].Item2) < epsilon)
        {
            // Save the tie-breaker
            if (TiebreakType == VotingTiebreakType.Random || tieBreaker == null)
            {
                results.TiebreakInFavourOf = new Random().NextDouble() < 0.5 ?
                    results.Results[0].Item1 :
                    results.Results[1].Item1;
            }
            else
            {
                var breakData = tieBreaker.ParsedVoteContent;

                var index1 = breakData.SelectedOptions.IndexOf(results.Results[0].Item1);
                var index2 = breakData.SelectedOptions.IndexOf(results.Results[1].Item1);

                if (index1 == -1 && index2 != -1)
                {
                    results.TiebreakInFavourOf = breakData.SelectedOptions[index2];
                }
                else if (index1 != -1 && index2 == -1)
                {
                    results.TiebreakInFavourOf = breakData.SelectedOptions[index1];
                }
                else if (index1 <= index2 && index1 != -1)
                {
                    results.TiebreakInFavourOf = breakData.SelectedOptions[index1];
                }
                else if (index2 < index1 && index2 != -1)
                {
                    results.TiebreakInFavourOf = breakData.SelectedOptions[index2];
                }
                else
                {
                    // No tie-breaker could be found...
                }
            }
        }

        PollResultsCreatedAt = DateTime.UtcNow;
        ParsedResults = results;
    }

    private void CalculateSimpleVotes(IEnumerable<MeetingPollVote> votes, Dictionary<int, double> countedVotes,
        out MeetingPollVote? tieBreaker, bool singleChoice)
    {
        tieBreaker = null;

        foreach (var vote in votes)
        {
            if (vote.IsTiebreaker)
                tieBreaker = vote;

            var parsedVote = vote.ParsedVoteContent;

            foreach (var choice in parsedVote.SelectedOptions)
            {
                countedVotes[choice] += vote.VotingPower;

                if (singleChoice)
                    break;
            }

            // TODO: can we report an error somehow if singleChoice is true and there were multiple votes?
        }
    }

    private void CalculateWeightedVotes(IEnumerable<MeetingPollVote> votes, Dictionary<int, double> countedVotes,
        out MeetingPollVote? tieBreaker)
    {
        tieBreaker = null;

        foreach (var vote in votes)
        {
            if (vote.IsTiebreaker)
                tieBreaker = vote;

            int voteNumber = 1;

            var parsedVote = vote.ParsedVoteContent;

            foreach (var choice in parsedVote.SelectedOptions)
            {
                double voteWeight = (double)vote.VotingPower / voteNumber;

                countedVotes[choice] += voteWeight;

                ++voteNumber;
            }
        }
    }
}
