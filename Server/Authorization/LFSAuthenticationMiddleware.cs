namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared;
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
                await WriteGitLFSJsonError(context, "Invalid format for Authorization header", true);
                return false;
            }

            var encoded = tokenValue.Split(' ').LastOrDefault();

            if (encoded == null)
            {
                await WriteGitLFSJsonError(context, "Invalid format for Authorization header", true);
                return false;
            }

            string userPassword;
            try
            {
                var base64EncodedBytes = Convert.FromBase64String(encoded);
                userPassword = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            }
            catch (Exception)
            {
                await WriteGitLFSJsonError(context, "Invalid encoding of Authorization header", true);
                return false;
            }

            var parts = userPassword.Split(':');

            if (parts.Length != 2)
            {
                await WriteGitLFSJsonError(context, "Invalid format for Authorization header", true);
                return false;
            }

            var user = await database.Users.WhereHashed(nameof(User.LfsToken), parts[1]).AsAsyncEnumerable()
                .FirstOrDefaultAsync(u => u.LfsToken == parts[1]);

            // The given "username" part of the basic auth needs to either match the email or name of the found user
            if (user != null && user.Suspended != true && (user.UserName == parts[0] || user.Email == parts[0]))
            {
                OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.LFSOnly, null);
                return true;
            }

            // If the token is incorrect we'll want to fail with 403 to not cause infinite retries in LFS clients
            await WriteGitLFSJsonError(context,
                "Invalid credentials (use your email and LFS token from your profile) or your account is suspended",
                false);
            return false;
        }

        /// <summary>
        ///   Sets the error for LFS login problem in LFS request
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     If this is edited the error response in <see cref="Controllers.LFSController"/> should be edited
        ///     as well.
        ///   </para>
        /// </remarks>
        private static Task WriteGitLFSJsonError(HttpContext context, string error, bool badRequest)
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
                new GitLFSErrorResponse() { Message = error }.ToString());
        }
    }
}
