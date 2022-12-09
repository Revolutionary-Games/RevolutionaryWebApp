namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;

[Index(nameof(Name), IsUnique = true)]
[Index(nameof(ReadAccess))]
public class Meeting : UpdateableModel, IUpdateNotifications
{
    [AllowSortingBy]
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Minutes { get; set; }

    [Required]
    public string Description { get; set; } = string.Empty;

    [AllowSortingBy]
    public DateTime StartsAt { get; set; }

    public TimeSpan? ExpectedDuration { get; set; }

    public TimeSpan JoinGracePeriod { get; set; } = TimeSpan.FromMinutes(20);

    public DateTime? EndedAt { get; set; }

    public AssociationResourceAccess ReadAccess { get; set; } = AssociationResourceAccess.Developers;
    public AssociationResourceAccess JoinAccess { get; set; } = AssociationResourceAccess.AssociationMembers;

    /// <summary>
    ///   Sometime after the meeting has ended (and potential minutes have been accepted) the data is locked from
    ///   further modification
    /// </summary>
    public bool ReadOnly { get; set; }

    public long? OwnerId { get; set; }
    public User? Owner { get; set; }

    public long? ChairmanId { get; set; }
    public User? Chairman { get; set; }

    public long? SecretaryId { get; set; }
    public User? Secretary { get; set; }

    public ICollection<MeetingMember> MeetingMembers { get; set; } = new HashSet<MeetingMember>();
    public ICollection<MeetingPoll> MeetingPolls { get; set; } = new HashSet<MeetingPoll>();
    public ICollection<MeetingPollVote> MeetingPollVotes { get; set; } = new HashSet<MeetingPollVote>();

    public ICollection<MeetingPollVotingRecord> MeetingPollVotingRecords { get; set; } =
        new HashSet<MeetingPollVotingRecord>();

    public MeetingDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            Minutes = Minutes,
            Description = Description,
            StartsAt = StartsAt,
            ExpectedDuration = ExpectedDuration,
            JoinGracePeriod = JoinGracePeriod,
            EndedAt = EndedAt,
            ReadAccess = ReadAccess,
            JoinAccess = JoinAccess,
            ReadOnly = ReadOnly,
            OwnerId = OwnerId,
            SecretaryId = SecretaryId,
            ChairmanId = ChairmanId,
        };
    }

    public MeetingInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            StartsAt = StartsAt,
            EndedAt = EndedAt,
            ReadAccess = ReadAccess,
            ReadOnly = ReadOnly,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // TODO: add all the groups here and listening for them
        // var info = GetInfo();
        /* yield return new Tuple<SerializedNotification, string>(new MeetingListUpdated()
                { Type = entityState.ToChangeType(), Item = info },
            NotificationGroups.MeetingListUpdatedPublic); */

        // TODO: different read accesses should probably send info to different groups
        yield return new Tuple<SerializedNotification, string>(
            new MeetingUpdated { Item = GetDTO() },
            NotificationGroups.MeetingUpdatedPrefix + Id);
    }
}
