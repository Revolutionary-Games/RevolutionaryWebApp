namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Shared;
using Shared.Models;

public class MeetingMember
{
    public long MeetingId { get; set; }

    [AllowSortingBy]
    public long UserId { get; set; }

    [AllowSortingBy]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public bool CanReviewMinutes { get; set; }

    [Timestamp]
    public uint Version { get; set; }

    public Meeting? Meeting { get; set; }
    public User? User { get; set; }

    public MeetingMemberDTO GetDTO()
    {
        return new()
        {
            MeetingId = MeetingId,
            UserId = UserId,
            JoinedAt = JoinedAt,
            CanReviewMinutes = CanReviewMinutes,
        };
    }

    public MeetingMemberInfo GetInfo()
    {
        return new()
        {
            MeetingId = MeetingId,
            UserId = UserId,
            JoinedAt = JoinedAt,
        };
    }
}
