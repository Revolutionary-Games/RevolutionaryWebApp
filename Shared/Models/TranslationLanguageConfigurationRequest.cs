namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

public sealed class TranslationLanguageConfigurationRequest
{
    [Required]
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;
}
