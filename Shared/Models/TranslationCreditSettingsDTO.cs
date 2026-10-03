namespace RevolutionaryWebApp.Shared.Models;

/// <summary>
///   Controls whether a contributor's email address appears in translation Git commits.
/// </summary>
public sealed class TranslationCreditSettingsDTO
{
    public bool UseEmailInTranslationCredits { get; set; }
}
