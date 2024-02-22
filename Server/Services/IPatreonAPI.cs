namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using RecursiveDataAnnotationsValidation;

/// <summary>
///   The V2 oauth patreon API, used when using oauth login, the creator API is used with a static creator token
/// </summary>
public interface IPatreonAPI
{
    /// <summary>
    ///   Setup the client for use
    /// </summary>
    /// <param name="clientId">The client ID patreon has given</param>
    /// <param name="clientSecret">The client secret from Patreon</param>
    public void Initialize(string clientId, string clientSecret);

    /// <summary>
    ///   Makes this client authenticate as the user
    ///   (doesn't perform any request, but future requests will use the tokens)
    /// </summary>
    /// <param name="token">The token to authenticate with</param>
    public void LoginAsUser(PatreonAPIBearerToken token);

    /// <summary>
    ///   Turns an OAuth login code into actual usable API tokens
    /// </summary>
    /// <param name="code">The code to turn into tokens</param>
    /// <param name="redirectUri">What return URI we should give Patreon</param>
    /// <returns>The token object</returns>
    public Task<PatreonAPIBearerToken> TurnCodeIntoTokens(string code, string redirectUri);

    /// <summary>
    ///   Gets details of current user
    /// </summary>
    /// <returns>The response object from Patreon</returns>
    public Task<PatreonAPIObjectResponse> GetOwnDetails();
}

public class PatreonAPI : IPatreonAPI
{
    private readonly Lazy<HttpClient> client = new();
    private string? clientId;
    private string? clientSecret;

    private HttpClient Client => client.Value;

    public void Initialize(string id, string secret)
    {
        clientId = id;
        clientSecret = secret;
    }

    public void LoginAsUser(PatreonAPIBearerToken? token)
    {
        client.Value.DefaultRequestHeaders.Authorization =
            token != null ? new AuthenticationHeaderValue("Bearer", token.AccessToken) : null;
    }

    public async Task<PatreonAPIBearerToken> TurnCodeIntoTokens(string code,
        string redirectUri)
    {
        if (clientId == null || clientSecret == null)
            throw new InvalidOperationException("API has not been initialized");

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
        });

        var response = await Client.PostAsync("https://www.patreon.com/api/oauth2/token", content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PatreonAPIBearerToken>();

        if (result == null || !Validator.TryValidateObject(result, new ValidationContext(result), null))
        {
            throw new InvalidDataException("invalid token object returned from Patreon API");
        }

        if (result.TokenType != "Bearer")
        {
            throw new InvalidDataException("non-bearer token type received from patreon");
        }

        return result;
    }

    public async Task<PatreonAPIObjectResponse> GetOwnDetails()
    {
        var result = await Client.GetFromJsonAsync<PatreonAPIObjectResponse>(QueryHelpers.AddQueryString(
            "https://www.patreon.com/api/oauth2/v2/identity",
            new Dictionary<string, string?>
            {
                { "fields[user]", "email,full_name,vanity,url" },
            }));

        var validator = new RecursiveDataAnnotationValidator();

        if (result == null || !validator.TryValidateObjectRecursive(result, new ValidationContext(result), null))
        {
            throw new InvalidDataException("invalid identity object returned from Patreon API");
        }

        return result;
    }
}

public class PatreonAPIDataException : Exception
{
    public PatreonAPIDataException(string message) : base(message)
    {
    }
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

public class PatreonObjectData
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Type { get; set; } = string.Empty;

    public PatreonObjectAttributes Attributes { get; set; } = new();

    public PatreonObjectRelationships? Relationships { get; set; } = new();
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
