namespace ThriveDevCenter.Shared.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class PollVoteData
{
    [Required]
    [MaxLength(200)]
    public List<int> SelectedOptions { get; set; } = new();
}
