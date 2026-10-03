namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

public sealed class CreateManualTranslationKeyRequest
{
    [Required]
    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(20000)]
    public string SourceText { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }
}
