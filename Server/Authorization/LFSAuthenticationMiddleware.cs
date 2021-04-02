namespace ThriveDevCenter.Server.Authorization
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    public class LFSAuthenticationMiddleware : BaseAuthenticationHelper
    {
        private readonly ApplicationDbContext database;

        public LFSAuthenticationMiddleware(ApplicationDbContext database)
        {
            this.database = database;
        }

        protected override async Task<bool> PerformAuthentication(HttpContext context)
        {
            // Check Authorization header for lfs token in "Basic base64encoded" format
            if (!context.Request.Headers.TryGetValue("Authorization", out StringValues header) || header.Count <= 0)
                return true;

            var tokenValue = header[0];

            if (!tokenValue.StartsWith("Basic "))
            {
                await WriteGitLFSJsonNError(context, "Invalid format for Authorization header", true);
                return false;
            }

            var encoded = tokenValue.Split(' ').LastOrDefault();

            if (encoded == null)
            {
                await WriteGitLFSJsonNError(context, "Invalid format for Authorization header", true);
                return false;
            }

            var base64EncodedBytes = System.Convert.FromBase64String(encoded);
            var userPassword = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);

            var parts = userPassword.Split(':');

            if (parts.Length != 2)
            {
                await WriteGitLFSJsonNError(context, "Invalid format for Authorization header", true);
                return false;
            }

            var user = await database.Users.WhereHashed(nameof(User.LfsToken), parts[1]).AsAsyncEnumerable()
                .FirstOrDefaultAsync(u => u.LfsToken == parts[1]);

            // The given "username" part of the basic auth needs to either match the email or name of the found user
            if (user != null && user.Suspended != true && (user.UserName == parts[0] || user.Email == parts[0]))
            {
                OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.LFSOnly);
                return true;
            }

            // If the token is incorrect we'll want to fail with 403 to not cause infinite retries in LFS clients
            await WriteGitLFSJsonNError(context,
                "Invalid credentials (use your email and LFS token from your profile) or you don't have write " +
                "access or your account is suspended", false);
            return false;
        }

        private Task WriteGitLFSJsonNError(HttpContext context, string error, bool badRequest)
        {
            context.Response.ContentType = AppInfo.GitLfsContentType;

            if (badRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
            else
            {
                context.Response.Headers["LFS-Authenticate"] = "Basic realm=\"ThriveDevCenter Git LFS\"";
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            }

            return context.Response.WriteAsync(
                new BasicJSONErrorResult(error, "For help see: https://wiki.revolutionarygamesstudio.com/wiki/Git_LFS")
                    .ToString());
        }
    }
}
