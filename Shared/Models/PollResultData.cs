namespace ThriveDevCenter.Shared.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class PollResultData
    {
        /// <summary>
        ///   The results of the poll. The first int is the id of the option and the second part of the tuple is the
        ///   total result for that. This list is in sorted order
        /// </summary>
        [Required]
        [MinLength(1)]
        public List<Tuple<int, double>> Results { get; set; } = new();

        /// <summary>
        ///   If the end result is a tie, then depending on the poll type a tiebreak is selected
        /// </summary>
        public int? TiebreakInFavourOf { get; set; }
    }
}
