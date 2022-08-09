namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using SharedBase.ModelVerifiers;

public class AssociationMemberDTO : ClientSideTimedModel
{
    [Required]
    [MaxLength(500)]
    [NoTrailingOrPrecedingSpace]
    public string FirstNames { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    [NoTrailingOrPrecedingSpace]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [Email]
    public string Email { get; set; } = string.Empty;

    [Required]
    public DateTime JoinDate { get; set; }

    [Required]
    [MaxLength(500)]
    public string CountryOfResidence { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string CityOfResidence { get; set; } = string.Empty;

    public long? UserId { get; set; }
    public bool BoardMember { get; set; }

    [DisallowIf(ThisMatches = "true", OtherProperty = nameof(BoardMember), IfOtherMatchesValue = "false",
        ErrorMessage = "If currently the president, must also be a board member")]
    [DisallowIfEnabled]
    public bool CurrentPresident { get; set; }

    [DisallowIf(ThisMatches = "false", OtherProperty = nameof(BoardMember), IfOtherMatchesValue = "true",
        ErrorMessage = "If currently a board member must have also been one in the past")]
    [DisallowIfEnabled]
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
            CurrentPresident = CurrentPresident,
            HasBeenBoardMember = HasBeenBoardMember,
            IsThriveDeveloper = IsThriveDeveloper,
        };
    }
}
