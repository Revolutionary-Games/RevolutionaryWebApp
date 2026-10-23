namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   One translation value and its source text for the editor.
/// </summary>
public sealed class TranslationEditorDTO
{
    public long ProjectId { get; set; }

    [MaxLength(120)]
    public string ProjectSlug { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string SourceText { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string SourceTextTranslatedFrom { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;

    public TranslationState State { get; set; }

    public Guid Version { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    [MaxLength(4000)]
    public string? SourceLocations { get; set; }

    public bool CanVerify { get; set; }
    public bool CanEditSource { get; set; }
    public int CommentCount { get; set; }
    public int SuggestionCount { get; set; }
}

/// <summary>
///   One entry in a language's translation list.
/// </summary>
public sealed class TranslationListItemDTO
{
    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string SourceText { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;

    public TranslationState State { get; set; }
}

/// <summary>
///   Save-request for one translation value.
/// </summary>
public sealed class TranslationSaveRequest
{
    public long ProjectId { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;

    public TranslationState State { get; set; } = TranslationState.Translated;

    public Guid Version { get; set; }
}
