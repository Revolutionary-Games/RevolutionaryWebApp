namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json.Serialization;
using Enums;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using Utilities;

/// <summary>
///   User's API key
/// </summary>
[Index(nameof(HashedId), IsUnique = true)]
public class APIKey : IContainsHashedLookUps, IUpdateNotifications
{
    [MaxLength(14)]
    [Key]
    [HashedLookUp]
    public string Id { get; set; } = string.Empty;

    public string? HashedId { get; set; }

    public long UserId { get; set; }
    public User User { get; set; } = null!;

    [JsonIgnore]
    [MaxLength(36)]
    public string Secret { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public string? LastUsedIP { get; set; }
    public long TotalUses { get; set; }

    public string? AllowedIPNetwork { get; set; }

    /// <summary>
    ///   Scopes the key is valid for. This default scope means "everything"
    /// </summary>
    public string Scopes { get; set; } = "0";

    public bool ReadOnly { get; set; } = true;

    public bool AllowsScope(APIKeyScope scope)
    {
        var scopes = Scopes.Split(',');
        return scopes.Contains("0") || scopes.Contains(((int)scope).ToString(CultureInfo.InvariantCulture));
    }

    public bool AllowsIP(IPAddress? address)
    {
        if (string.IsNullOrEmpty(AllowedIPNetwork))
            return true;

        if (address == null || !IPNetwork.TryParse(AllowedIPNetwork, out var network))
            return false;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return network.Contains(address);
    }

    public APIKeyDTO GetDTO()
    {
        return new APIKeyDTO
        {
            Id = Id,
            CreatedAt = CreatedAt,
            ExpiresAt = ExpiresAt,
            LastUsedAt = LastUsedAt,
            LastUsedIP = LastUsedIP,
            TotalUses = TotalUses,
            AllowedIPNetwork = AllowedIPNetwork,
            Scopes = Scopes.Split(',').Select(s => (APIKeyScope)int.Parse(s)).ToArray(),
            ReadOnly = ReadOnly,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new APIKeyListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = GetDTO(),
            UserId = UserId,
        }, NotificationGroups.UserAPIKeysUpdatedPrefix + UserId);
    }
}
