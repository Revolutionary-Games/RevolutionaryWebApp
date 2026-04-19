namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using SharedBase.Utilities;

/// <summary>
///   Token used for password reset links. Encodes the target user email.
/// </summary>
public class PasswordResetToken
{
    public const string ProtectionPurpose = "PasswordResetToken.v1";
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    [JsonConstructor]
    public PasswordResetToken(string email)
    {
        Email = email;
    }

    /// <summary>
    ///   Email address to reset password for.
    /// </summary>
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; }

    public static PasswordResetToken? TryToLoadFromString(ITimeLimitedDataProtector dataProtector, string tokenStr)
    {
        try
        {
            var unprotected = dataProtector.Unprotect(tokenStr);
            return JsonSerializer.Deserialize<PasswordResetToken>(unprotected);
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

    public string ToEncodedString(ITimeLimitedDataProtector dataProtector, TimeSpan? lifetime = null)
    {
        var payload = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

        return dataProtector.Protect(payload, lifetime ?? DefaultLifetime);
    }
}
