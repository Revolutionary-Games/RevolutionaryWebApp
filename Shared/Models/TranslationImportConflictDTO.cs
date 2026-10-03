namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public sealed class TranslationImportConflictDTO
{
    public long Id { get; set; }

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string LocalText { get; set; } = string.Empty;

    [MaxLength(20000)]
    public string IncomingText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
