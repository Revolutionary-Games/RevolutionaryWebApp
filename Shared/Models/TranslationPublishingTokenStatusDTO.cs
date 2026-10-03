namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
///   Whether a GitHub token for translation publishing has been configured.
/// </summary>
public sealed class TranslationPublishingTokenStatusDTO
{
    public bool Configured { get; set; }
}

/// <summary>
///   Sets the GitHub token for translation publishing. The token is never returned by the API.
/// </summary>
public sealed class SetTranslationPublishingTokenRequest
{
    [Required]
    [MaxLength(500)]
    public string Token { get; set; } = string.Empty;
}
