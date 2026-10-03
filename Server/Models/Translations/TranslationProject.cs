namespace RevolutionaryWebApp.Server.Models.Translations;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared.Models;

[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(RepositoryFullName), nameof(SourceBranch))]
public class TranslationProject
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [MaxLength(35)]
    public string SourceLanguage { get; set; } = "en";

    public TranslationFormat Format { get; set; }
    public long? DictionaryId { get; set; }
    public TranslationDictionary? Dictionary { get; set; }

    [MaxLength(240)]
    public string? RepositoryFullName { get; set; }

    [MaxLength(160)]
    public string? SourceBranch { get; set; }

    [MaxLength(500)]
    public string? SourceFilePattern { get; set; }

    [MaxLength(500)]
    public string? TranslationFilePattern { get; set; }

    [MaxLength(80)]
    public string? LastImportedSha { get; set; }

    public DateTime? LastImportedAt { get; set; }
    public long CurrentRevision { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TranslationProjectLanguage> Languages { get; set; } = new HashSet<TranslationProjectLanguage>();
    public ICollection<TranslationUnit> Units { get; set; } = new HashSet<TranslationUnit>();
    public ICollection<TranslationSyncRun> SyncRuns { get; set; } = new HashSet<TranslationSyncRun>();
    public TranslationPullRequest? PullRequest { get; set; }
}

public class TranslationProjectLanguage
{
    public long ProjectId { get; set; }

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? FilePath { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTime? LastImportedAt { get; set; }

    public TranslationProject Project { get; set; } = null!;
    public ICollection<TranslationValue> Values { get; set; } = new HashSet<TranslationValue>();
    public ICollection<TranslationSubscription> Subscriptions { get; set; } = new HashSet<TranslationSubscription>();
}

[Index(nameof(ProjectId), nameof(Active))]
public class TranslationUnit
{
    public long ProjectId { get; set; }

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string SourceText { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string? SourcePluralText { get; set; }

    [MaxLength(4000)]
    public string? Context { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    [MaxLength(4000)]
    public string? SourceLocations { get; set; }

    [MaxLength(64)]
    public string SourceHash { get; set; } = string.Empty;

    public long SourceRevision { get; set; }
    public bool Active { get; set; } = true;
    public DateTime SourceUpdatedAt { get; set; } = DateTime.UtcNow;

    public TranslationProject Project { get; set; } = null!;
    public ICollection<TranslationValue> Values { get; set; } = new HashSet<TranslationValue>();
}

[Index(nameof(ProjectId), nameof(LanguageCode), nameof(State))]
[Index(nameof(ProjectId), nameof(PendingSince))]
public class TranslationValue
{
    public long ProjectId { get; set; }

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string LastImportedText { get; set; } = string.Empty;

    [MaxLength(100000)]
    public string? PluralFormsJson { get; set; }

    public TranslationState State { get; set; }
    public long SourceRevisionTranslatedFrom { get; set; }

    [MaxLength(20000)]
    public string SourceTextTranslatedFrom { get; set; } = string.Empty;

    public long? LastEditedByUserId { get; set; }
    public DateTime? LastEditedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public long? PendingRevision { get; set; }
    public DateTime? PendingSince { get; set; }
    public long? LastPublishedRevision { get; set; }

    [Timestamp]
    public uint Xmin { get; set; }

    public TranslationUnit Unit { get; set; } = null!;
    public TranslationProjectLanguage Language { get; set; } = null!;
    public User? LastEditedByUser { get; set; }

    public ICollection<TranslationImportConflict> ImportConflicts { get; set; } =
        new HashSet<TranslationImportConflict>();

    public ICollection<TranslationChange> Changes { get; set; } = new HashSet<TranslationChange>();
    public ICollection<TranslationComment> Comments { get; set; } = new HashSet<TranslationComment>();
    public ICollection<TranslationSuggestion> Suggestions { get; set; } = new HashSet<TranslationSuggestion>();
}

[Index(nameof(ProjectId), nameof(Key), nameof(LanguageCode), nameof(ResolvedAt))]
public class TranslationImportConflict
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string LocalText { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string IncomingText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    public TranslationValue Value { get; set; } = null!;
}

[Index(nameof(ProjectId), nameof(Key), nameof(LanguageCode), nameof(CreatedAt))]
public class TranslationChange
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    public long? ActorUserId { get; set; }
    public long? ContributorUserId { get; set; }
    public TranslationChangeKind Kind { get; set; }

    [MaxLength(20000)]
    public string OldText { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string NewText { get; set; } = string.Empty;

    public TranslationState OldState { get; set; }
    public TranslationState NewState { get; set; }
    public long SourceRevision { get; set; }
    public long ProjectRevision { get; set; }
    public bool CountsAsContribution { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TranslationValue Value { get; set; } = null!;
    public User? ActorUser { get; set; }
    public User? ContributorUser { get; set; }
}

[Index(nameof(ProjectId), nameof(Key), nameof(LanguageCode), nameof(CreatedAt))]
public class TranslationComment
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    public long AuthorUserId { get; set; }

    [MaxLength(10000)]
    public string RawMarkdown { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public bool Deleted { get; set; }

    public TranslationValue Value { get; set; } = null!;
    public User AuthorUser { get; set; } = null!;
}

[Index(nameof(Status), nameof(CreatedAt))]
public class TranslationSuggestion
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    public long AuthorUserId { get; set; }

    [MaxLength(20000)]
    public string Text { get; set; } = string.Empty;

    public long SourceRevision { get; set; }
    public TranslationSuggestionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public long? ResolvedByUserId { get; set; }

    public TranslationValue Value { get; set; } = null!;
    public User AuthorUser { get; set; } = null!;
    public User? ResolvedByUser { get; set; }
    public ICollection<TranslationSuggestionVote> Votes { get; set; } = new HashSet<TranslationSuggestionVote>();
}

public class TranslationSuggestionVote
{
    public long SuggestionId { get; set; }
    public long UserId { get; set; }
    public int Value { get; set; }

    public TranslationSuggestion Suggestion { get; set; } = null!;
    public User User { get; set; } = null!;
}

public class TranslationSubscription
{
    public long UserId { get; set; }
    public long ProjectId { get; set; }

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(40)]
    public string EventType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public TranslationProjectLanguage ProjectLanguage { get; set; } = null!;
}

[Index(nameof(ProjectId), nameof(StartedAt))]
public class TranslationSyncRun
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    [MaxLength(30)]
    public string Type { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Error { get; set; }

    [MaxLength(80)]
    public string? UpstreamSha { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public TranslationProject Project { get; set; } = null!;
}

public class TranslationPullRequest
{
    public long ProjectId { get; set; }
    public int Number { get; set; }

    [MaxLength(160)]
    public string HeadBranch { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? HeadSha { get; set; }

    public bool Open { get; set; }
    public long LastPublishedRevision { get; set; }
    public DateTime? LastCheckedAt { get; set; }

    public TranslationProject Project { get; set; } = null!;
}
