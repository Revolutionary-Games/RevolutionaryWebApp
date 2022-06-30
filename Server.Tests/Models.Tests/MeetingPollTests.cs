namespace ThriveDevCenter.Server.Tests.Models.Tests
{
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
                        { 3, new PollData.PollChoice(3, "Name3") }
                    },
                    WeightedChoices = new PollData.WeightedChoicesList(),
                }
            };

            var votes = new List<MeetingPollVote>()
            {
                new MeetingPollVote()
                {
                    IsTiebreaker = true,
                    VotingPower = 2,
                    ParsedVoteContent = new PollVoteData()
                    {
                        SelectedOptions = new List<int>() { 1, 2 }
                    }
                },
                new MeetingPollVote()
                {
                    ParsedVoteContent = new PollVoteData()
                    {
                        SelectedOptions = new List<int>() { 2 }
                    }
                },
                new MeetingPollVote()
                {
                    ParsedVoteContent = new PollVoteData()
                    {
                        SelectedOptions = new List<int>() { 3, 2, 1 }
                    }
                },
                new MeetingPollVote()
                {
                    VotingPower = 2,
                    ParsedVoteContent = new PollVoteData()
                    {
                        SelectedOptions = new List<int>() { 3 }
                    }
                },
            };

            poll.CalculateResults(votes);

            Assert.NotNull(poll.PollResults);

            var resultData = poll.ParsedResults;

            Assert.NotNull(resultData);
            Assert.NotNull(resultData!.Results);
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
    }
}
