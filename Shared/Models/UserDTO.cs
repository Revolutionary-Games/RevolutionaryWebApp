namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class UserDTO : ClientSideTimedModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool Local { get; set; }
    public string? SsoSource { get; set; }

    [Required]
    public CachedUserGroups Groups { get; set; } = null!;

    public bool HasApiToken { get; set; }
    public bool HasLfsToken { get; set; }
    public int TotalLauncherLinks { get; set; }

    public DateTime? SuspendedUntil { get; set; }
    public string? SuspendedReason { get; set; }
    public bool SuspendedManually { get; set; }

    public bool AssociationMember { get; set; }
    public bool BoardMember { get; set; }
    public bool HasBeenBoardMember { get; set; }
}
