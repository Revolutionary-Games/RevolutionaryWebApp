namespace ThriveDevCenter.Shared.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

public class PollResultData
{
    /// <summary>
    ///   The results of the poll. The first int is the id of the option and the second part of the tuple is the
    ///   total result for that. This list is in sorted order
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<ChoiceVotes> Results { get; set; } = new();

    [Required]
    public double TotalVotes { get; set; }

    /// <summary>
    ///   If the end result is a tie, then depending on the poll type a tiebreak is selected
    /// </summary>
    public int? TiebreakInFavourOf { get; set; }

    /// <summary>
    ///   Holds the votes for a choice. This is required as on the client <see cref="Tuple{T1,T2}"/>
    ///   with int and double doesn't deserialize from JSON correctly
    /// </summary>
    public class ChoiceVotes
    {
        private int choiceId;
        private double votes;

        [JsonConstructor]
        public ChoiceVotes(int item1, double item2)
        {
            ChoiceId = item1;
            Votes = item2;
        }

        // The properties are setup like this to be compatible with older data
        [JsonInclude]
        public int Item1
        {
            get => choiceId;
            private set => choiceId = value;
        }

        [JsonInclude]
        public double Item2
        {
            get => votes;
            private set => votes = value;
        }

        [JsonIgnore]
        public int ChoiceId
        {
            get => choiceId;
            private set => choiceId = value;
        }

        [JsonIgnore]
        public double Votes
        {
            get => votes;
            private set => votes = value;
        }
    }
}