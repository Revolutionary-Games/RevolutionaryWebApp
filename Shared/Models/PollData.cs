namespace ThriveDevCenter.Shared.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class PollData
{
    /// <summary>
    ///   The choices given in the poll
    /// </summary>
    [Required]
    [MinLength(2)]
    [MaxLength(200)]
    public Dictionary<int, PollChoice> Choices { get; set; } = new();

    // Only one of the types should be populated to detect which kind of poll this is
    public WeightedChoicesList? WeightedChoices { get; set; }

    /// <summary>
    ///   In the poll is a list of choices that the user can put in a certain order. Not all items need to be ranked
    ///   but all that are will be given voting power / nth choice as the weight.
    /// </summary>
    public class WeightedChoicesList
    {
        // TODO: implement this option
        [Required]
        public bool CanSelectNone { get; set; } = false;
    }

    public SingleChoice? SingleChoiceOption { get; set; }

    /// <summary>
    ///   The poll voter can only select one choice. The choice is given voting power of the user
    /// </summary>
    public class SingleChoice
    {
        [Required]
        public bool CanSelectNone { get; set; }
    }

    public MultipleChoice? MultipleChoiceOption { get; set; }

    /// <summary>
    ///   The poll voter can select one or more choices (up to a limit). All of the choices is given a
    /// </summary>
    public class MultipleChoice
    {
        [Required]
        [Range(0, 200)]
        public int MinimumSelections { get; set; }

        [Required]
        [Range(2, 200)]
        public int MaximumSelections { get; set; }
    }

    public class PollChoice
    {
        public PollChoice(int id, string name)
        {
            Id = id;
            Name = name;
        }

        [Required]
        [Range(1, 1000)]
        public int Id { get; set; }

        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string Name { get; set; }
    }
}