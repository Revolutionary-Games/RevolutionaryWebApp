namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;

// TODO: this should be renamed to UserDTO as this is closer to that than the actual Info types
public class UserInfo : ClientSideTimedModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool Local { get; set; }
    public string? SsoSource { get; set; }

    public bool Developer { get; set; }
    public bool Admin { get; set; }

    public bool Restricted { get; set; }

    /// <summary>
    ///   Precomputed access level from the server
    /// </summary>
    public UserAccessLevel AccessLevel { get; set; }

    public bool HasApiToken { get; set; }
    public bool HasLfsToken { get; set; }
    public int TotalLauncherLinks { get; set; }

    public bool Suspended { get; set; }
    public string? SuspendedReason { get; set; }
    public bool SuspendedManually { get; set; }

    public int SessionVersion { get; set; }
    public bool AssociationMember { get; set; }
    public bool BoardMember { get; set; }
    public bool HasBeenBoardMember { get; set; }
}