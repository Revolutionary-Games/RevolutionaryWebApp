namespace RevolutionaryWebApp.Server.Utilities;

using System;
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
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(90);

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

    /// <summary>
    ///   Decode a token using a time-limited data protector. Returns null if the token is expired or invalid.
    /// </summary>
    public static EmailPreferenceToken? TryToLoadFromString(ITimeLimitedDataProtector dataProtector, string tokenStr)
    {
        try
        {
            // Will throw if token is invalid or expired
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
    ///   Creates a protected, time-limited string representation suitable for URLs.
    /// </summary>
    /// <param name="dataProtector">Time-limited protector</param>
    /// <param name="lifetime">Optional lifetime, defaults to 90 days</param>
    public string ToEncodedString(ITimeLimitedDataProtector dataProtector, TimeSpan? lifetime = null)
    {
        var payload = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

        return dataProtector.Protect(payload, lifetime ?? DefaultLifetime);
    }
}
