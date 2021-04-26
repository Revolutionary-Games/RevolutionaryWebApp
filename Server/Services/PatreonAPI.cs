namespace ThriveDevCenter.Server.Services
{
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

    public class PatreonAPI : IPatreonAPI
    {
        private readonly Lazy<HttpClient> client = new Lazy<HttpClient>();
        private string clientId;
        private string clientSecret;

        private HttpClient Client => client.Value;

        public void Initialize(string id, string secret)
        {
            clientId = id;
            clientSecret = secret;
        }

        public void LoginAsUser(PatreonAPIBearerToken token)
        {
            client.Value.DefaultRequestHeaders.Authorization =
                token != null ? new AuthenticationHeaderValue("Bearer", token.AccessToken) : null;
        }

        public async Task<PatreonAPIBearerToken> TurnCodeIntoTokens(string code,
            string redirectUri)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("redirect_uri", redirectUri)
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
                new Dictionary<string, string>()
                {
                    { "fields[user]", "email,full_name,vanity,url" }
                }));

            if (result == null || !Validator.TryValidateObject(result, new ValidationContext(result), null))
            {
                throw new InvalidDataException("invalid identity object returned from Patreon API");
            }

            return result;
        }
    }

    public interface IPatreonAPI
    {
        /// <summary>
        ///   Setup the client for use
        /// </summary>
        /// <param name="clientId">The client ID patreon has given</param>
        /// <param name="clientSecret">The client secret from Patreon</param>
        void Initialize(string clientId, string clientSecret);

        /// <summary>
        ///   Makes this client authenticate as the user
        ///   (doesn't perform any request, but future requests will use the tokens)
        /// </summary>
        /// <param name="token">The token to authenticate with</param>
        void LoginAsUser(PatreonAPIBearerToken token);

        /// <summary>
        ///   Turns an OAuth login code into actual usable API tokens
        /// </summary>
        /// <param name="code">The code to turn into tokens</param>
        /// <param name="redirectUri">What return URI we should give Patreon</param>
        /// <returns>The token object</returns>
        Task<PatreonAPIBearerToken> TurnCodeIntoTokens(string code, string redirectUri);

        /// <summary>
        ///   Gets details of current user
        /// </summary>
        /// <returns>The response object from Patreon</returns>
        Task<PatreonAPIObjectResponse> GetOwnDetails();
    }

    public class PatreonAPIBearerToken
    {
        [Required]
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; set; }

        public string Scope { get; set; }

        [Required]
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }

    public class PatreonAPIObjectResponse
    {
        [Required]
        public PatreonObjectData Data { get; set; }

        public List<PatreonObjectData> Included { get; set; } = new();
        public Dictionary<string, string> Links { get; set; } = new();

        public PatreonObjectData FindIncludedObject(string objectId)
        {
            foreach (var item in Included)
            {
                if (item.Id == objectId)
                    return item;
            }

            return null;
        }
    }

    public class PatreonObjectData
    {
        [Required]
        public string Id { get; set; }

        [Required]
        public string Type { get; set; }

        public Dictionary<string, string> Attributes { get; set; } = new();

        public Dictionary<string, PatreonRelationshipInfo> Relationships { get; set; } = new();
    }

    public class PatreonRelationshipInfo
    {
        [Required]
        public PatreonObjectData Data { get; set; }

        public Dictionary<string, string> Links { get; set; } = new();
    }
}
