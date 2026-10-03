namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   A proposed translation and its community vote score.
/// </summary>
public sealed class TranslationSuggestionDTO
{
    public long Id { get; set; }
    public long AuthorUserId { get; set; }

    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;

    public int Score { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
///   Creates a translation suggestion.
/// </summary>
public sealed class CreateTranslationSuggestionRequest
{
    public long ProjectId { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
///   Votes -1, 0, or +1; zero removes the existing vote.
/// </summary>
public sealed class VoteOnTranslationSuggestionRequest
{
    public int Value { get; set; }
}
