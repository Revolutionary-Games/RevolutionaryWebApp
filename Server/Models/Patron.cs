namespace RevolutionaryWebApp.Server.Models;

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;

[Index(nameof(Email), IsUnique = true)]
[Index(nameof(EmailAlias), IsUnique = true)]
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

    [Required]
    public string RewardId { get; set; } = string.Empty;

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
            RewardId = RewardId,
            HasForumAccount = HasForumAccount ?? false,
            Suspended = Suspended ?? false,
        };
    }
}
