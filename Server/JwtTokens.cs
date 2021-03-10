namespace ThriveDevCenter.Server
{
    using System;
    using System.IdentityModel.Tokens.Jwt;
    using System.Text;
    using Microsoft.Extensions.Configuration;
    using Microsoft.IdentityModel.Tokens;

    public class JwtTokens
    {
        private const string issuer = "ThriveDevCenter";

        private readonly byte[] csrfSecret;
        private readonly int csrfExpiry;

        public JwtTokens(IConfiguration configuration)
        {
            string secret = configuration.GetValue<string>("CSRF:Secret");

            if (string.IsNullOrEmpty(secret))
                throw new ArgumentException("no CSRF token secret defined");

            csrfSecret = Encoding.UTF8.GetBytes(secret);

            csrfExpiry = configuration.GetValue<int>("CSRF:Expiry");
        }

        // TODO: should this take in the current user id? to make previous pages not valid after login?
        // Though that might be more annoying than the extra security it gives is worth
        public string GenerateCSRFToken()
        {
            // TODO: passing notBefore is probably not needed as no one would be able to get the token
            // before we generate it...
            var token = new JwtSecurityToken(issuer, string.Empty, null, DateTime.UtcNow, GetCSRFTokenExpiry(),
                new SigningCredentials(new SymmetricSecurityKey(csrfSecret), SecurityAlgorithms.HmacSha256Signature));

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            return tokenString;
        }

        public DateTime GetCSRFTokenExpiry()
        {
            return DateTime.UtcNow + TimeSpan.FromSeconds(csrfExpiry);
        }
    }
}
