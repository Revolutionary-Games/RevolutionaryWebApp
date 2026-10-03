namespace RevolutionaryWebApp.Server.Models.Translations;

using System;
using System.ComponentModel.DataAnnotations;
using Shared.Models;

public class TranslationDictionary
{
    [Key]
    public long Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;
}

public class DictionaryTerm
{
    public long DictionaryId { get; set; }

    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Explanation { get; set; }

    public long? LastEditedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DictionaryTermTranslation
{
    public long DictionaryId { get; set; }

    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Explanation { get; set; }

    public TranslationState State { get; set; }
    public long? LastEditedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class DictionaryTermRequest
{
    public long Id { get; set; }
    public long DictionaryId { get; set; }

    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Explanation { get; set; }

    public long RequesterUserId { get; set; }
    public bool? Approved { get; set; }
    public long? DecidedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}
