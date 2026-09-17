namespace RevolutionaryWebApp.Server.Models;

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;

[Index(nameof(Email), IsUnique = true)]
[Index(nameof(EmailAlias), IsUnique = true)]
[Index(nameof(PatreonUserId), IsUnique = true)]
[Index(nameof(PatreonMemberId), IsUnique = true)]
public class Patron : UpdateableModel
{
    [Required]
    [AllowSortingBy]
    public string Email { get; set; } = string.Empty;

    // TODO: add restriction that email alias can't be a value in Email
    [AllowSortingBy]
    public string? EmailAlias { get; set; }

    [Required]
    [AllowSortingBy]
    public string Username { get; set; } = string.Empty;

    [AllowSortingBy]
    public int PledgeAmountCents { get; set; }

    // TODO: add pledge currency here

    /// <summary>
    ///   The main tier ID that the patron is entitled to.
    /// </summary>
    [Required]
    public string TierId { get; set; } = string.Empty;

    /// <summary>
    ///   A comma separated list of tier ids that the patron is entitled to.
    /// </summary>
    public string EntitledTierIds { get; set; } = string.Empty;

    public string? PatreonUserId { get; set; }
    public string? PatreonMemberId { get; set; }

    public bool? Marked { get; set; } = true;

    public string? PatreonToken { get; set; }
    public string? PatreonRefreshToken { get; set; }

    public bool? HasForumAccount { get; set; } = false;

    [AllowSortingBy]
    public bool? Suspended { get; set; } = false;
    public string? SuspendedReason { get; set; }

    public PatronDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Email = Email,
            EmailAlias = EmailAlias,
            Username = Username,
            PledgeAmountCents = PledgeAmountCents,
            TierId = TierId,
            HasForumAccount = HasForumAccount ?? false,
            Suspended = Suspended ?? false,
        };
    }
}
