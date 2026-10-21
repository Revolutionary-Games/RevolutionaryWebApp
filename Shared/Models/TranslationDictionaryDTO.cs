namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
///   A dictionary available to one or more translation projects.
/// </summary>
public sealed class TranslationDictionaryDTO
{
    public long Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
///   A dictionary word and its translation for one language.
/// </summary>
public sealed class DictionaryTermDTO
{
    public long DictionaryId { get; set; }

    /// <summary>
    ///   The language this word belongs to when it is a language-specific term without an English source.
    /// </summary>
    public string SourceLanguageCode { get; set; } = "en";

    public bool IsLanguageSpecific { get; set; }

    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? SourceExplanation { get; set; }

    [MaxLength(4000)]
    public string? TargetExplanation { get; set; }

    [MaxLength(20000)]
    public string TargetText { get; set; } = string.Empty;

    public TranslationState State { get; set; }

    public long? LastEditedByUserId { get; set; }
}

/// <summary>
///   Adds a term that is specific to one non-English dictionary language.
/// </summary>
public sealed class CreateDictionaryLanguageTermRequest
{
    public long DictionaryId { get; set; }

    [Required]
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Explanation { get; set; }
}

/// <summary>
///   Creates a dictionary.
/// </summary>
public sealed class CreateDictionaryRequest
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
///   Creates or proposes an English dictionary term.
/// </summary>
public sealed class CreateDictionaryTermRequest
{
    public long DictionaryId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Explanation { get; set; }
}

/// <summary>
///   Edits a target-language dictionary term.
/// </summary>
public sealed class SaveDictionaryTranslationRequest
{
    public long DictionaryId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [Required]
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Explanation { get; set; }

    public TranslationState State { get; set; } = TranslationState.Translated;
}

/// <summary>
///   A pending request for an English dictionary word.
/// </summary>
public sealed class DictionaryTermRequestDTO
{
    public long Id { get; set; }
    public long DictionaryId { get; set; }

    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Explanation { get; set; }

    public long RequesterUserId { get; set; }
}
