namespace ThriveDevCenter.Server
{
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

    public class JwtTokens
    {
        private readonly ILogger<JwtTokens> logger;
        private const string Issuer = "ThriveDevCenter";

        private readonly int csrfExpiry;
        private readonly TokenValidationParameters validationParameters;
        private readonly SigningCredentials signingCredentials;

        public JwtTokens(IConfiguration configuration, ILogger<JwtTokens> logger)
        {
            this.logger = logger;
            string secret = configuration.GetValue<string>("CSRF:Secret");

            if (string.IsNullOrEmpty(secret))
                throw new ArgumentException("no CSRF token secret defined");

            var csrfSecret = Encoding.UTF8.GetBytes(secret);

            csrfExpiry = configuration.GetValue<int>("CSRF:Expiry");

            // Setup the signing and verifying
            signingCredentials = new SigningCredentials(new SymmetricSecurityKey(csrfSecret),
                SecurityAlgorithms.HmacSha256Signature);

            validationParameters = new TokenValidationParameters()
            {
                // The default seems to allow a bit expired tokens, so a more strict check is used
                LifetimeValidator = (notBefore, expires, securityToken, parameters) =>
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
                ValidIssuer = Issuer,
                IssuerSigningKey = new SymmetricSecurityKey(csrfSecret),
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256Signature }
            };
        }

        public string GenerateCSRFToken(User user)
        {
            var claims = new List<Claim>()
            {
                new Claim("LoggedIn", user != null ? "true" : "false"),
                new Claim("UserId", UserIdFromPotentiallyNull(user))
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

        public bool IsValidCSRFToken(string tokenString, User requiredUser, bool verifyUser = true)
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
                    if (principal.Claims.First(c => c.Type == "UserId").Value !=
                        UserIdFromPotentiallyNull(requiredUser))
                    {
                        throw new ArgumentException("UserId contained in token doesn't match required user id");
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

        private string UserIdFromPotentiallyNull(User user)
        {
            if (user == null)
                return "null";

            return user.Id.ToString();
        }
    }
}
