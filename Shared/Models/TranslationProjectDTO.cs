namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

/// <summary>
///   One translation project shown in the public catalog.
/// </summary>
public sealed class TranslationProjectDTO : IIdentifiable
{
    public long Id { get; set; }

    public bool ClosedForMaintenance { get; set; }

    /// <summary>
    ///   A project operation currently holds the lock that translation saves need.
    /// </summary>
    public bool OperationInProgress { get; set; }

    public TranslationAnnouncementDTO? Announcement { get; set; }

    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(35)]
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>
    ///   Directory URL against which relative source locations are resolved.
    /// </summary>
    [MaxLength(2000)]
    public string? SourceLocationBaseUrl { get; set; }

    /// <summary>
    ///   Relative URL template using {path} and {line}; defaults to {path}#L{line}.
    /// </summary>
    [MaxLength(500)]
    public string? SourceLocationUrlTemplate { get; set; }

    public TranslationFormat Format { get; set; }
    public TranslationQualityCheck QualityChecks { get; set; }

    public long? DictionaryId { get; set; }

    /// <summary>
    ///   This is actually the current user permission status.
    /// </summary>
    public bool CanAddSourceKeys { get; set; }
}

/// <summary>
///   One language configured for a translation project.
/// </summary>
public sealed class TranslationLanguageDTO
{
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    public int Total { get; set; }
    public int NeedsTranslation { get; set; }
    public int Translated { get; set; }
    public int Verified { get; set; }
    public int CheckFailures { get; set; }
}
