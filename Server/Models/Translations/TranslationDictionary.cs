namespace RevolutionaryWebApp.Server.Models.Translations;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared.Models;

[Index(nameof(Name), IsUnique = true)]
public class TranslationDictionary
{
    [Key]
    public long Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public ICollection<TranslationProject> Projects { get; set; } = new HashSet<TranslationProject>();
    public ICollection<DictionaryTerm> Terms { get; set; } = new HashSet<DictionaryTerm>();

    /// <summary>
    ///   Language-specific terms for this dictionary (so ones that don't exist for English, but just for one language)
    /// </summary>
    public ICollection<DictionaryLanguageTerm> LanguageTerms { get; set; } = new HashSet<DictionaryLanguageTerm>();

    public ICollection<DictionaryTermRequest> Requests { get; set; } = new HashSet<DictionaryTermRequest>();
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

    public TranslationDictionary Dictionary { get; set; } = null!;
    public User? LastEditedByUser { get; set; }
    public ICollection<DictionaryTermTranslation> Translations { get; set; } = new HashSet<DictionaryTermTranslation>();
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

    [Timestamp]
    public uint Xmin { get; set; }

    public DictionaryTerm Term { get; set; } = null!;
    public User? LastEditedByUser { get; set; }
}

/// <summary>
///   A term that belongs to a specific language and has no English source term.
/// </summary>
public class DictionaryLanguageTerm
{
    public long DictionaryId { get; set; }

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Explanation { get; set; }

    public long? LastEditedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public TranslationDictionary Dictionary { get; set; } = null!;
    public User? LastEditedByUser { get; set; }
}

[Index(nameof(DictionaryId), nameof(Approved), nameof(CreatedAt))]
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

    public TranslationDictionary Dictionary { get; set; } = null!;
    public User RequesterUser { get; set; } = null!;
    public User? DecidedByUser { get; set; }
}
