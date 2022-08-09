namespace ThriveDevCenter.Server.Tests.Models.Tests;

using System;
using System.Collections.Generic;
using Server.Models;
using Shared.Models;
using Xunit;

public class MeetingPollTests
{
    [Fact]
    public void Poll_WeightedChoicesAreCorrectlyCounted()
    {
        var poll = new MeetingPoll()
        {
            ClosedAt = DateTime.UtcNow,
            ParsedData = new PollData()
            {
                Choices = new Dictionary<int, PollData.PollChoice>()
                {
                    { 1, new PollData.PollChoice(1, "Name1") },
                    { 2, new PollData.PollChoice(2, "Name2") },
                    { 3, new PollData.PollChoice(3, "Name3") },
                },
                WeightedChoices = new PollData.WeightedChoicesList(),
            },
        };

        var votes = new List<MeetingPollVote>()
        {
            new()
            {
                IsTiebreaker = true,
                VotingPower = 2,
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 1, 2 },
                },
            },
            new()
            {
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 2 },
                },
            },
            new()
            {
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 3, 2, 1 },
                },
            },
            new()
            {
                VotingPower = 2,
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 3 },
                },
            },
        };

        poll.CalculateResults(votes);

        Assert.NotNull(poll.PollResults);

        var resultData = poll.ParsedResults;

        Assert.NotNull(resultData);
        Assert.NotNull(resultData.Results);
        Assert.Null(resultData.TiebreakInFavourOf);
        Assert.Equal(7.833333333333334, resultData.TotalVotes);

        Assert.Equal(3, resultData.Results.Count);

        // Results check

        Assert.Equal(3, resultData.Results[0].Item1);
        Assert.Equal(3, resultData.Results[0].Item2);

        Assert.Equal(2, resultData.Results[1].Item1);
        Assert.Equal(2.5, resultData.Results[1].Item2);

        Assert.Equal(1, resultData.Results[2].Item1);
        Assert.Equal(2.3333333333333335, resultData.Results[2].Item2);
    }

    [Fact]
    public void Poll_SingleChoicePollCountingWorks()
    {
        var poll = new MeetingPoll()
        {
            ClosedAt = DateTime.UtcNow,
            ParsedData = new PollData()
            {
                Choices = new Dictionary<int, PollData.PollChoice>()
                {
                    { 1, new PollData.PollChoice(1, "Name1") },
                    { 2, new PollData.PollChoice(2, "Name2") },
                    { 3, new PollData.PollChoice(3, "Name3") },
                },
                SingleChoiceOption = new PollData.SingleChoice(),
            },
        };

        var votes = new List<MeetingPollVote>()
        {
            new()
            {
                IsTiebreaker = true,
                VotingPower = 2,
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 1 },
                },
            },
            new()
            {
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 2 },
                },
            },
            new()
            {
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 3 },
                },
            },
            new()
            {
                VotingPower = 2,
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 3 },
                },
            },
        };

        poll.CalculateResults(votes);

        Assert.NotNull(poll.PollResults);

        var resultData = poll.ParsedResults;

        Assert.NotNull(resultData);
        Assert.NotNull(resultData.Results);
        Assert.Null(resultData.TiebreakInFavourOf);
        Assert.Equal(6, resultData.TotalVotes);

        Assert.Equal(3, resultData.Results.Count);

        // Results check

        Assert.Equal(3, resultData.Results[0].Item1);
        Assert.Equal(3, resultData.Results[0].Item2);

        Assert.Equal(1, resultData.Results[1].Item1);
        Assert.Equal(2, resultData.Results[1].Item2);

        Assert.Equal(2, resultData.Results[2].Item1);
        Assert.Equal(1, resultData.Results[2].Item2);
    }

    /// <summary>
    ///   Makes sure that if somehow multiple votes for the single poll type are voted, they are not counted
    /// </summary>
    [Fact]
    public void Poll_SinglePollMultipleVotesAreNotCounted()
    {
        var poll = new MeetingPoll()
        {
            ClosedAt = DateTime.UtcNow,
            ParsedData = new PollData()
            {
                Choices = new Dictionary<int, PollData.PollChoice>()
                {
                    { 1, new PollData.PollChoice(1, "Name1") },
                    { 2, new PollData.PollChoice(2, "Name2") },
                    { 3, new PollData.PollChoice(3, "Name3") },
                },
                SingleChoiceOption = new PollData.SingleChoice(),
            },
        };

        var votes = new List<MeetingPollVote>()
        {
            new()
            {
                IsTiebreaker = true,
                VotingPower = 2,
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 1, 2 },
                },
            },
            new()
            {
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 2 },
                },
            },
            new()
            {
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 3 },
                },
            },
            new()
            {
                VotingPower = 2,
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 3, 1 },
                },
            },
        };

        poll.CalculateResults(votes);

        Assert.NotNull(poll.PollResults);

        var resultData = poll.ParsedResults;

        Assert.NotNull(resultData);
        Assert.NotNull(resultData.Results);
        Assert.Null(resultData.TiebreakInFavourOf);
        Assert.Equal(6, resultData.TotalVotes);

        Assert.Equal(3, resultData.Results.Count);

        // Results check

        Assert.Equal(3, resultData.Results[0].Item1);
        Assert.Equal(3, resultData.Results[0].Item2);

        Assert.Equal(1, resultData.Results[1].Item1);
        Assert.Equal(2, resultData.Results[1].Item2);

        Assert.Equal(2, resultData.Results[2].Item1);
        Assert.Equal(1, resultData.Results[2].Item2);
    }

    [Fact]
    public void Poll_MultipleChoiceResultsAreRight()
    {
        var poll = new MeetingPoll()
        {
            ClosedAt = DateTime.UtcNow,
            ParsedData = new PollData()
            {
                Choices = new Dictionary<int, PollData.PollChoice>()
                {
                    { 1, new PollData.PollChoice(1, "Name1") },
                    { 2, new PollData.PollChoice(2, "Name2") },
                    { 3, new PollData.PollChoice(3, "Name3") },
                },
                MultipleChoiceOption = new PollData.MultipleChoice(),
            },
        };

        var votes = new List<MeetingPollVote>()
        {
            new()
            {
                IsTiebreaker = true,
                VotingPower = 2,
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 1, 2 },
                },
            },
            new()
            {
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 2 },
                },
            },
            new()
            {
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 3, 2, 1 },
                },
            },
            new()
            {
                VotingPower = 2,
                ParsedVoteContent = new PollVoteData()
                {
                    SelectedOptions = new List<int>() { 3 },
                },
            },
        };

        poll.CalculateResults(votes);

        Assert.NotNull(poll.PollResults);

        var resultData = poll.ParsedResults;

        Assert.NotNull(resultData);
        Assert.NotNull(resultData.Results);
        Assert.Null(resultData.TiebreakInFavourOf);
        Assert.Equal(10, resultData.TotalVotes);

        Assert.Equal(3, resultData.Results.Count);

        // Results check

        Assert.Equal(2, resultData.Results[0].Item1);
        Assert.Equal(4, resultData.Results[0].Item2);

        Assert.Equal(1, resultData.Results[1].Item1);
        Assert.Equal(3, resultData.Results[1].Item2);

        Assert.Equal(3, resultData.Results[2].Item1);
        Assert.Equal(3, resultData.Results[2].Item2);
    }

    [Fact]
    public void Poll_ParsingResultDataWorks()
    {
        var poll = new MeetingPoll()
        {
            ClosedAt = DateTime.UtcNow,
            PollResults =
                @"{""Results"":[{""Item1"":1,""Item2"":5},{""Item1"":2,""Item2"":1}],""TotalVotes"":6,""TiebreakInFavourOf"":null}",
        };

        var results = poll.ParsedResults;

        Assert.NotNull(results);
        Assert.Equal(2, results.Results.Count);

        var dto = poll.GetDTO();

        results = dto.ParsedResults;

        Assert.NotNull(results);
        Assert.Equal(2, results.Results.Count);
    }
}