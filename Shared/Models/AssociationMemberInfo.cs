namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class AssociationMemberInfo : ClientSideTimedModel
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public DateTime JoinDate { get; set; }

    public long? UserId { get; set; }
    public bool BoardMember { get; set; }
}
