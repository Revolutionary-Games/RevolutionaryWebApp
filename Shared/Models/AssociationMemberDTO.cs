namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class AssociationMemberDTO : ClientSideTimedModel
{
    [Required]
    [MaxLength(500)]
    public string FirstNames { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [StringLength(AppInfo.MaxEmailLength, MinimumLength = AppInfo.MinEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public DateOnly JoinDate { get; set; }

    [Required]
    [MaxLength(500)]
    public string CountryOfResidence { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string CityOfResidence { get; set; } = string.Empty;

    public long? UserId { get; set; }
    public bool BoardMember { get; set; }
    public bool HasBeenBoardMember { get; set; }
    public bool IsThriveDeveloper { get; set; }

    public AssociationMemberDTO Clone()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            FirstNames = FirstNames,
            LastName = LastName,
            Email = Email,
            JoinDate = JoinDate,
            CountryOfResidence = CountryOfResidence,
            CityOfResidence = CityOfResidence,
            UserId = UserId,
            BoardMember = BoardMember,
            HasBeenBoardMember = HasBeenBoardMember,
            IsThriveDeveloper = IsThriveDeveloper,
        };
    }
}
