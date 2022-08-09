namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Models;

public abstract class JwtBase
{
    protected const string Issuer = "ThriveDevCenter";
    protected readonly byte[] CSRFSecret;

    protected JwtBase(IConfiguration configuration)
    {
        string secret = configuration.GetValue<string>("CSRF:Secret");

        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("no CSRF token secret defined");

        CSRFSecret = Encoding.UTF8.GetBytes(secret);
    }

    protected string UserIdFromPotentiallyNull(User? user)
    {
        if (user == null)
            return "null";

        return user.Id.ToString();
    }
}

public interface ITokenVerifier
{
    bool IsValidCSRFToken(string tokenString, User? requiredUser, bool verifyUser = true);
}

public interface ITokenGenerator
{
    string GenerateCSRFToken(User? user);
    DateTime GetCSRFTokenExpiry();
}

public class TokenGenerator : JwtBase, ITokenGenerator
{
    private readonly int csrfExpiry;
    private readonly SigningCredentials signingCredentials;

    public TokenGenerator(IConfiguration configuration) : base(configuration)
    {
        csrfExpiry = configuration.GetValue<int>("CSRF:Expiry");

        signingCredentials = new SigningCredentials(new SymmetricSecurityKey(CSRFSecret),
            SecurityAlgorithms.HmacSha256Signature);
    }

    public string GenerateCSRFToken(User? user)
    {
        var claims = new List<Claim>
        {
            new Claim("LoggedIn", user != null ? "true" : "false"),
            new Claim("UserId", UserIdFromPotentiallyNull(user)),
        };

        var token = new JwtSecurityToken(Issuer, string.Empty, claims, null, GetCSRFTokenExpiry(),
            signingCredentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return tokenString;
    }

    public DateTime GetCSRFTokenExpiry()
    {
        return DateTime.UtcNow + TimeSpan.FromSeconds(csrfExpiry);
    }
}

public class TokenVerifier : JwtBase, ITokenVerifier
{
    private readonly ILogger<TokenVerifier> logger;

    private readonly TokenValidationParameters validationParameters;

    public TokenVerifier(IConfiguration configuration, ILogger<TokenVerifier> logger) : base(configuration)
    {
        this.logger = logger;

        validationParameters = CreateValidationParameters(CSRFSecret, Issuer);
    }

    public static TokenValidationParameters CreateValidationParameters(byte[] secret, string issuer)
    {
        return new()
        {
            // The default seems to allow a bit expired tokens, so a more strict check is used
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = DateTime.UtcNow;

                if (notBefore > now)
                    return false;

                // Expires is required
                if (expires == null || expires < now)
                    return false;

                return true;
            },
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidIssuer = issuer,
            IssuerSigningKey = new SymmetricSecurityKey(secret),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256Signature },
        };
    }

    public bool IsValidCSRFToken(string tokenString, User? requiredUser, bool verifyUser = true)
    {
        var tokenHandler = new JwtSecurityTokenHandler();

        try
        {
            var principal = tokenHandler.ValidateToken(tokenString, validationParameters, out var validatedToken);

            if (validatedToken == null)
                return false;

            if (verifyUser)
            {
                // In some rare cases (like signup), we don't really care who is making the request
                var claimUserId = principal.Claims.First(c => c.Type == "UserId").Value;
                var requiredId = UserIdFromPotentiallyNull(requiredUser);
                if (claimUserId != requiredId)
                {
                    throw new ArgumentException($"UserId contained in token ({claimUserId}) doesn't " +
                        $"match required user id ({requiredId})");
                }
            }
        }
        catch (Exception e)
        {
            // Maybe would be nice to catch only the specific exceptions that indicate specific problems...
            logger.LogWarning("Invalid CSRF token was checked: {@E}", e);
            return false;
        }

        return true;
    }
}