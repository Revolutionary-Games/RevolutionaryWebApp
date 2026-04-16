namespace RevolutionaryWebApp.Server.Utilities;

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using SharedBase.Utilities;

/// <summary>
///   Token that allows managing email preferences without logging in. Encodes the target email.
/// </summary>
public class EmailPreferenceToken
{
    public const string ProtectionPurpose = "EmailPreferenceToken.v1";

    [JsonConstructor]
    public EmailPreferenceToken(string email)
    {
        Email = email;
    }

    /// <summary>
    ///   Email address the preferences apply to.
    /// </summary>
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; }

    public static EmailPreferenceToken? TryToLoadFromString(IDataProtector dataProtector, string tokenStr)
    {
        try
        {
            var unprotected = dataProtector.Unprotect(tokenStr);
            return JsonSerializer.Deserialize<EmailPreferenceToken>(unprotected);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    ///   Creates a protected string representation suitable for URLs.
    /// </summary>
    /// <returns>Encoded and protected token</returns>
    public string ToEncodedString(IDataProtector dataProtector)
    {
        return dataProtector.Protect(JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        }));
    }
}
