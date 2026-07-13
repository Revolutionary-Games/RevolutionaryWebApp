namespace RevolutionaryWebApp.Shared.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

// This file has various data objects the Patreon API can return

public class PatreonObjectData
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Type { get; set; } = string.Empty;

    public PatreonObjectAttributes Attributes { get; set; } = new();

    public PatreonObjectRelationships? Relationships { get; set; } = new();
}

public class PatreonAPIBearerToken
{
    [Required]
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }

    public string? Scope { get; set; }

    [Required]
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}

public class PatreonAPIBaseResponse
{
    public List<PatreonObjectData> Included { get; set; } = new();
    public Dictionary<string, string>? Links { get; set; } = new();
    public PatreonAPIMeta Meta { get; set; } = new();

    public PatreonObjectData? FindIncludedObject(string objectId, string? neededType = null)
    {
        foreach (var item in Included)
        {
            if (item.Id == objectId)
            {
                if (neededType != null && item.Type != neededType)
                    continue;

                return item;
            }
        }

        return null;
    }
}

public class PatreonAPIMeta
{
    public int Count { get; set; }
}

public class PatreonAPIObjectResponse : PatreonAPIBaseResponse
{
    [Required]
    public PatreonObjectData Data { get; set; } = new();
}

public class PatreonAPIListResponse : PatreonAPIBaseResponse
{
    [Required]
    public List<PatreonObjectData> Data { get; set; } = new();
}

// The next two classes are split due to the data being non-uniform coming from patreon
public class PatreonObjectAttributes
{
    public string? Email { get; set; }

    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    public string? Vanity { get; set; }

    [JsonPropertyName("declined_since")]
    public string? DeclinedSince { get; set; }

    [JsonPropertyName("amount_cents")]
    public int? AmountCents { get; set; }

    public string? Currency { get; set; }

    [JsonPropertyName("pledge_cap_cents")]
    public int? PledgeCapCents { get; set; }

    // V2 fields
    public string? Title { get; set; }
}

public class PatreonObjectRelationships
{
    public PatreonRelationshipInfo? Patron { get; set; }
    public PatreonRelationshipInfo? Reward { get; set; }

    public PatreonRelationshipInfo? Creator { get; set; }

    public PatreonObjectDataList? Rewards { get; set; }
    public PatreonObjectDataList? Goals { get; set; }
}

public class PatreonRelationshipInfo
{
    [Required]
    public PatreonObjectData? Data { get; set; }

    public Dictionary<string, string> Links { get; set; } = new();
}

public class PatreonObjectDataList
{
    [Required]
    public List<PatreonObjectData> Data { get; set; } = new();
}

public class PatronMemberInfo
{
    public PatreonObjectData? Pledge { get; set; }
    public PatreonObjectData? User { get; set; }
    public PatreonObjectData? Reward { get; set; }
}
