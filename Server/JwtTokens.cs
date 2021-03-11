namespace ThriveDevCenter.Server
{
    using System;
    using System.IdentityModel.Tokens.Jwt;
    using System.Text;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.Tokens;

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

                    if (expires < now)
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

        // TODO: should this take in the current user id? to make previous pages not valid after login?
        // Though that might be more annoying than the extra security it gives is worth
        public string GenerateCSRFToken()
        {
            var token = new JwtSecurityToken(Issuer, string.Empty, null, null, GetCSRFTokenExpiry(),
                signingCredentials);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            return tokenString;
        }

        public DateTime GetCSRFTokenExpiry()
        {
            return DateTime.UtcNow + TimeSpan.FromSeconds(csrfExpiry);
        }

        public bool IsValidCSRFToken(string tokenString)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            try
            {
                var principal = tokenHandler.ValidateToken(tokenString, validationParameters, out var validatedToken);

                if (validatedToken == null)
                    return false;
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
}
