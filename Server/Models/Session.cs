namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Enums;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using SharedBase.Converters;
using Utilities;

[Index(nameof(UserId))]
[Index(nameof(HashedId), IsUnique = true)]
public class Session : IContainsHashedLookUps, IUpdateNotifications
{
    [Key]
    [HashedLookUp]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? HashedId { get; set; }

    public long? UserId { get; set; }
    public User? User { get; set; }

    public string? CachedUserGroupsRaw { get; set; }

    public string? SsoNonce { get; set; }
    public string? StartedSsoLogin { get; set; }

    public string? SsoReturnUrl { get; set; }

    /// <summary>
    ///   Used to timeout started sso requests
    /// </summary>
    public DateTime? SsoStartTime { get; set; }

    /// <summary>
    ///   Used to clear old sessions
    /// </summary>
    [AllowSortingBy]
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///   Used also to clear old sessions to enforce total session duration TODO: implement that job
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? LastUsedFrom { get; set; }

    public InProgressClaSignature? InProgressClaSignature { get; set; }

    /// <summary>
    ///   The user groups associated with this session
    /// </summary>
    [NotMapped]
    public CachedUserGroups? CachedUserGroups
    {
        get
        {
            if (CachedUserGroupsRaw == null || UserId == null)
                return new CachedUserGroups(GroupType.NotLoggedIn);

            return JsonSerializer.Deserialize<CachedUserGroups>(CachedUserGroupsRaw);
        }
        set
        {
            // The last check here makes non-logged in users never have groups. That needs to be removed if that
            // assumption is not correct in the future.
            if (value == null || UserId == null || !value.HasAccessLevel(GroupType.RestrictedUser))
            {
                CachedUserGroupsRaw = null;
                return;
            }

            CachedUserGroupsRaw = JsonSerializer.Serialize(value);
        }
    }

    public bool IsCloseToExpiry()
    {
        return DateTime.UtcNow - LastUsed > TimeSpan.FromSeconds(AppInfo.SessionExpirySeconds - 3600 * 8);
    }

    public long GetDoubleHashedId()
    {
        return SelectByHashedProperty.DoubleHashAsIdStandIn(Id.ToString(), HashedId);
    }

    public SessionDTO GetDTO(bool current)
    {
        return new()
        {
            Id = GetDoubleHashedId(),
            CreatedAt = StartedAt,
            UpdatedAt = LastUsed,
            LastUsedFrom = LastUsedFrom,
            Current = current,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // Only user ID based session listening is possible, so if this session doesn't belong to an user
        // we can skip sending any updates
        if (UserId == null)
            yield break;

        yield return new Tuple<SerializedNotification, string>(new SessionListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(false),
        }, NotificationGroups.UserSessionsUpdatedPrefix + UserId);
    }
}
