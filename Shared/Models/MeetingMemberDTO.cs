namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.Text.Json.Serialization;
using DevCenterCommunication.Models;

public class MeetingMemberDTO : IIdentifiable
{
    public long MeetingId { get; set; }
    public long UserId { get; set; }
    public DateTime JoinedAt { get; set; }
    public bool CanReviewMinutes { get; set; }

    [JsonIgnore]
    public long Id => (UserId << 24) + MeetingId;
}
