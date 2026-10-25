namespace RevolutionaryWebApp.Server.Models.Translations;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models;
using Shared.Notifications;

[Index(nameof(Name), IsUnique = true)]
public class TranslationDictionary : IUpdateNotifications
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

    public TranslationDictionaryDTO GetDTO()
    {
        return new TranslationDictionaryDTO { Id = Id, Name = Name };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return TranslationNotificationHelper.DictionaryUpdated(Id);
        yield return TranslationNotificationHelper.DictionaryListUpdated();
    }
}

public class DictionaryTerm : IUpdateNotifications, IHasUpdatedAt
{
    public long DictionaryId { get; set; }

    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Explanation { get; set; }

    public long? LastEditedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public TranslationDictionary Dictionary { get; set; } = null!;
    public User? LastEditedByUser { get; set; }
    public ICollection<DictionaryTermTranslation> Translations { get; set; } = new HashSet<DictionaryTermTranslation>();

    public DictionaryTermDTO GetDTO(DictionaryTermTranslation? translation = null)
    {
        return new DictionaryTermDTO
        {
            DictionaryId = DictionaryId,
            Word = Word,
            Context = Context,
            SourceExplanation = Explanation,
            TargetExplanation = translation?.Explanation,
            TargetText = translation?.Text ?? string.Empty,
            State = translation?.State ?? TranslationState.NeedsTranslation,
            LastEditedByUserId = translation?.LastEditedByUserId,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        if (entityState is EntityState.Added or EntityState.Deleted)
        {
            yield return TranslationNotificationHelper.DictionaryTermsUpdated(DictionaryId);
        }
        else
        {
            yield return TranslationNotificationHelper.DictionaryTermUpdated(DictionaryId, Word, Context, "en");
        }
    }
}

public class DictionaryTermTranslation : IUpdateNotifications, IHasUpdatedAt
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();

    [Timestamp]
    public uint Xmin { get; set; }

    public DictionaryTerm Term { get; set; } = null!;
    public User? LastEditedByUser { get; set; }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // New target rows are created together with their source term, which already sends the list event.
        if (entityState != EntityState.Added)
            yield return TranslationNotificationHelper.DictionaryTermUpdated(DictionaryId, Word, Context, LanguageCode);
    }
}

/// <summary>
///   A target-language term with a source word used to find it in translation text.
///   The reason why this exists is that people cannot just add anything they want to the English dictionary
///   (they need to request it). So instead if there are some not very important words, but still useful in a
///   language people can add language-specific dictionary entries that don't go into other languages (and English).
/// </summary>
public class DictionaryLanguageTerm : IUpdateNotifications, IHasUpdatedAt
{
    public long DictionaryId { get; set; }

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Word { get; set; } = string.Empty;

    [MaxLength(250)]
    public string SourceWord { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Context { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Explanation { get; set; }

    public long? LastEditedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public TranslationDictionary Dictionary { get; set; } = null!;
    public User? LastEditedByUser { get; set; }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        if (entityState is EntityState.Added or EntityState.Deleted)
        {
            yield return TranslationNotificationHelper.DictionaryTermsUpdated(DictionaryId);
        }
        else
        {
            yield return TranslationNotificationHelper.DictionaryTermUpdated(DictionaryId, Word, Context, LanguageCode);
        }
    }
}

[Index(nameof(DictionaryId), nameof(Approved), nameof(CreatedAt))]
public class DictionaryTermRequest : IUpdateNotifications
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

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return TranslationNotificationHelper.DictionaryRequestsUpdated();
    }
}
