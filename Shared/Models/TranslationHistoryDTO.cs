namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   One historical translation edit.
/// </summary>
public sealed class TranslationHistoryDTO
{
    public long Id { get; set; }
    public long? ActorUserId { get; set; }

    public TranslationChangeKind Kind { get; set; }

    [MaxLength(20000)]
    public string OldText { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string NewText { get; set; } = string.Empty;

    public TranslationState OldState { get; set; }

    public TranslationState NewState { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
///   One raw Markdown translation comment.
/// </summary>
public sealed class TranslationCommentDTO
{
    public long Id { get; set; }
    public long AuthorUserId { get; set; }

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(10000)]
    public string RawMarkdown { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>
///   Creates a comment attached to one translated string.
/// </summary>
public sealed class CreateTranslationCommentRequest
{
    public long ProjectId { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    public bool ForEveryone { get; set; }

    [Required]
    [MaxLength(10000)]
    public string RawMarkdown { get; set; } = string.Empty;
}

/// <summary>
///   How another language translated the same source key.
/// </summary>
public sealed class NearbyTranslationDTO
{
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;

    public TranslationState State { get; set; }
}
