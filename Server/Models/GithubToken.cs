namespace RevolutionaryWebApp.Server.Models;

using System.ComponentModel.DataAnnotations;
using Shared.Models.Enums;

public class GithubToken : ModelWithCreationTime
{
    public const string TranslationsProtectionPurpose = "TranslationPublishingGitHubToken.v1";

    [Required]
    public GithubTokenPurpose Purpose { get; set; }

    [Required]
    [MaxLength(1024)]
    public string Ciphertext { get; set; } = string.Empty;
}
