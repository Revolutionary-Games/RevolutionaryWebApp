namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class AssociationMemberInfo : ClientSideTimedModel
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public DateOnly JoinDate { get; set; }

    public long? UserId { get; set; }
    public bool BoardMember { get; set; }
}
