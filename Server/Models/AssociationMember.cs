namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(Email), IsUnique = true)]
[Index(nameof(UserId), IsUnique = true)]
public class AssociationMember : UpdateableModel, IUpdateNotifications
{
    public AssociationMember(string firstNames, string lastName, string email, DateOnly joinDate,
        string countryOfResidence, string cityOfResidence)
    {
        FirstNames = firstNames;
        LastName = lastName;
        Email = email;
        JoinDate = joinDate;
        CountryOfResidence = countryOfResidence;
        CityOfResidence = cityOfResidence;
    }

    [Required]
    [UpdateFromClientRequest]
    public string FirstNames { get; set; }

    [Required]
    [UpdateFromClientRequest]
    public string LastName { get; set; }

    [Required]
    [AllowSortingBy]
    public string Email { get; set; }

    [Required]
    [AllowSortingBy]
    [UpdateFromClientRequest]
    [ConvertWithWhenUpdatingFromClient(nameof(DateTimeToDateOnly))]
    public DateOnly JoinDate { get; set; }

    [Required]
    [UpdateFromClientRequest]
    public string CountryOfResidence { get; set; }

    [Required]
    [UpdateFromClientRequest]
    public string CityOfResidence { get; set; }

    /// <summary>
    ///   Associated user account (if any)
    /// </summary>
    public long? UserId { get; set; }

    public User? User { get; set; }

    [UpdateFromClientRequest]
    public bool BoardMember { get; set; }

    [UpdateFromClientRequest]
    public bool CurrentPresident { get; set; }

    [UpdateFromClientRequest]
    public bool HasBeenBoardMember { get; set; }

    [UpdateFromClientRequest]
    public bool IsThriveDeveloper { get; set; }

    public static DateOnly DateTimeToDateOnly(DateTime value)
    {
        return DateOnly.FromDateTime(value);
    }

    public AssociationMemberDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            FirstNames = FirstNames,
            LastName = LastName,
            Email = Email,
            JoinDate = JoinDate.ToDateTime(new TimeOnly(0, 0)),
            CountryOfResidence = CountryOfResidence,
            CityOfResidence = CityOfResidence,
            UserId = UserId,
            BoardMember = BoardMember,
            CurrentPresident = CurrentPresident,
            HasBeenBoardMember = HasBeenBoardMember,
            IsThriveDeveloper = IsThriveDeveloper,
        };
    }

    public AssociationMemberInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Email = Email,
            JoinDate = JoinDate.ToDateTime(new TimeOnly(0, 0)),
            UserId = UserId,
            BoardMember = BoardMember,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(
            new AssociationMemberListUpdated { Type = entityState.ToChangeType(), Item = GetInfo() },
            NotificationGroups.AssociationMemberListUpdated);

        // For slightly better control on the data single item update notifications with all data are not enabled
    }
}
