namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Enums;

/// <summary>
///   Public information about a configured GitHub token. The token value is never returned.
/// </summary>
public sealed class GithubTokenDTO
{
    public long Id { get; set; }

    public GithubTokenPurpose Purpose { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
///   Creates a GitHub token for the selected purpose. The token value is write-only.
/// </summary>
public sealed class CreateGithubTokenRequestDTO
{
    [EnumDataType(typeof(GithubTokenPurpose))]
    public GithubTokenPurpose Purpose { get; set; }

    [Required]
    [MaxLength(500)]
    public string Token { get; set; } = string.Empty;
}
