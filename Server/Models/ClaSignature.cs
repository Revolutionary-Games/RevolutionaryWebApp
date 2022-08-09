namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;

[Index(nameof(ClaId), nameof(Email))]
[Index(nameof(ClaId), nameof(GithubAccount))]
[Index(nameof(ClaSignatureStoragePath), IsUnique = true)]
[Index(nameof(ClaInvalidationStoragePath), IsUnique = true)]
public class ClaSignature : BaseModel
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [AllowSortingBy]
    public DateTime? ValidUntil { get; set; }

    [Required]
    [AllowSortingBy]
    public string Email { get; set; } = string.Empty;

    [AllowSortingBy]
    public string? GithubAccount { get; set; }

    public long? GithubUserId { get; set; }

    public string? GithubEmail { get; set; }

    public string? DeveloperUsername { get; set; }

    [Required]
    public string ClaSignatureStoragePath { get; set; } = string.Empty;

    public string? ClaInvalidationStoragePath { get; set; }

    [AllowSortingBy]
    public long ClaId { get; set; }

    [AllowSortingBy]
    public long? UserId { get; set; }

    public Cla? Cla { get; set; }

    public User? User { get; set; }

    public CLASignatureDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            ValidUntil = ValidUntil,
            Email = Email,
            GithubAccount = GithubAccount,
            GithubUserId = GithubUserId,
            DeveloperUsername = DeveloperUsername,
            ClaId = ClaId,
            UserId = UserId,
        };
    }

    public CLASignatureSearchResult ToSearchResult(bool allowEmail, bool allowGithub)
    {
        return new()
        {
            CreatedAt = CreatedAt,
            DeveloperUsername = DeveloperUsername,
            Email = allowEmail ? Email : null,
            GithubAccount = allowGithub ? GithubAccount : null,
        };
    }
}
