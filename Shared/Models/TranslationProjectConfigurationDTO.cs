namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Editable configuration for a translation project backed by a public GitHub repository.
/// </summary>
public sealed class TranslationProjectConfigurationDTO
{
    public long Id { get; set; }

    [Required]
    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(35)]
    public string SourceLanguage { get; set; } = "en";

    public TranslationFormat Format { get; set; } = TranslationFormat.Gettext;

    [MaxLength(240)]
    public string RepositoryFullName { get; set; } = string.Empty;

    [MaxLength(160)]
    public string SourceBranch { get; set; } = "master";

    [MaxLength(500)]
    public string SourceFilePath { get; set; } = string.Empty;

    [MaxLength(500)]
    public string TranslationFilePattern { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
    public long? DictionaryId { get; set; }
    public DateTimeOffset? LastImportedAt { get; set; }

    [MaxLength(80)]
    public string? LastImportedSha { get; set; }
}
