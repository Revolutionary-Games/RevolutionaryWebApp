namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
///   Notification subscription choices for one project and language.
/// </summary>
public sealed class TranslationSubscriptionDTO
{
    public long ProjectId { get; set; }

    [Required]
    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    public bool NewKeys { get; set; }
    public bool TranslationUpdates { get; set; }
}

/// <summary>
///   Whether to offer translation subscriptions after several contributions.
/// </summary>
public sealed class TranslationSubscriptionPromptDTO
{
    public bool ShowPrompt { get; set; }
}
