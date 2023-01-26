namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Models;

public class EmailTokens
{
    private const string Issuer = "ThriveDevCenter-mail";

    private readonly ILogger<EmailTokens> logger;

    private readonly TokenValidationParameters validationParameters;
    private readonly SigningCredentials signingCredentials;

    public EmailTokens(ILogger<EmailTokens> logger, IConfiguration configuration)
    {
        this.logger = logger;

        string? secret = configuration.GetValue<string>("Email:TokenSecret");

        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("no email token secret defined");

        var csrfSecret = Encoding.UTF8.GetBytes(secret);

        validationParameters = TokenVerifier.CreateValidationParameters(csrfSecret, Issuer);

        signingCredentials = new SigningCredentials(new SymmetricSecurityKey(csrfSecret),
            SecurityAlgorithms.HmacSha256Signature);
    }

    public string GenerateToken(EmailTokenData data, TimeSpan? expiryTime = null)
    {
        expiryTime ??= TimeSpan.FromHours(4);

        var claims = new List<Claim>
        {
            new("Type", "email"),
            new("Data", JsonSerializer.Serialize(data)),
        };

        var token = new JwtSecurityToken(Issuer, string.Empty, claims, null,
            DateTime.UtcNow + expiryTime, signingCredentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return tokenString;
    }

    public EmailTokenData? ReadAndVerify(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken == null)
                return null;

            if (!principal.Claims.Any(c => c.Type == "Type" && c.Value == "email"))
                throw new ArgumentException("Token is not a valid email token");

            var data = JsonSerializer.Deserialize<EmailTokenData>(principal.Claims.First(c => c.Type == "Data")
                .Value);

            if (data == null)
                throw new NullReferenceException("json parsing returned null");

            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(data, new ValidationContext(data), errors))
            {
                throw new ArgumentException("Email token data didn't pass validation");
            }

            return data;
        }
        catch (Exception e)
        {
            logger.LogWarning("Invalid email token was checked: {@E}", e);
            return null;
        }
    }
}
