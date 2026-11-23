namespace RevolutionaryWebApp.Shared.Models;

/// <summary>
///   A contributor's lifetime translation credit for one project, including superseded translations.
/// </summary>
public sealed class TranslationCreditDTO
{
    public string Name { get; set; } = string.Empty;

    public int ContributionCount { get; set; }

    public string Email { get; set; } = string.Empty;
}
