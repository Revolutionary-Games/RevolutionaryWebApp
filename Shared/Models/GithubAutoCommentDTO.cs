namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using Enums;

public class GithubAutoCommentDTO : ClientSideTimedModel
{
    public bool Enabled { get; set; }

    [MaxLength(500)]
    public string? Repository { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 5)]
    public string CommentText { get; set; } = string.Empty;

    public AutoCommentCondition Condition { get; set; }
}