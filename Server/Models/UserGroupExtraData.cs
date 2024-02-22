namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevCenterCommunication.Models;
using Shared;
using Shared.Models.Enums;

/// <summary>
///   Extra data about a group that is only used when viewing the group, this is separated to make it cheaper to
///   have the group data retrieved for user
/// </summary>
public class UserGroupExtraData : ITimestampedModel
{
    public UserGroupExtraData(GroupType groupId, DateTime createdAt, DateTime updatedAt)
    {
        GroupId = groupId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    [Key]
    [AllowSortingBy]
    public GroupType GroupId { get; set; }

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; }

    [AllowSortingBy]
    public DateTime UpdatedAt { get; set; }

    [NotMapped]
    public long Id
    {
        get => (long)GroupId;
        set
        {
            GroupId = (GroupType)value;
        }
    }

    public string? CustomDescription { get; set; }

    public UserGroup Group { get; set; } = null!;
}
