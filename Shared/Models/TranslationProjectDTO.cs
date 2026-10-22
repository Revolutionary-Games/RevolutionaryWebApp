namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
///   One translation project shown in the public catalog.
/// </summary>
public sealed class TranslationProjectDTO
{
    public long Id { get; set; }

    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(35)]
    public string SourceLanguage { get; set; } = string.Empty;

    public TranslationFormat Format { get; set; }

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
}
