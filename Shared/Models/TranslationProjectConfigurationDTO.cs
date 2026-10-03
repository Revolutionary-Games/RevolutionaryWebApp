namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Editable configuration for a translation project backed by a public GitHub repository.
/// </summary>
public sealed record TranslationProjectConfigurationDTO
{
    public long Id { get; init; }

    [Required]
    [MaxLength(120)]
    public string Slug { get; init; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [MaxLength(35)]
    public string SourceLanguage { get; init; } = "en";

    [Required]
    public string Format { get; init; } = "Gettext";

    [Required]
    [MaxLength(240)]
    public string RepositoryFullName { get; init; } = string.Empty;

    [Required]
    [MaxLength(160)]
    public string SourceBranch { get; init; } = "master";

    [Required]
    [MaxLength(500)]
    public string SourceFilePath { get; init; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string TranslationFilePattern { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;
    public long? DictionaryId { get; init; }
    public DateTimeOffset? LastImportedAt { get; init; }
    public string? LastImportedSha { get; init; }
}
