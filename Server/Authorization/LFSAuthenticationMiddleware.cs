namespace ThriveDevCenter.Server.Authorization
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Models;

    public class LFSAuthenticationMiddleware : IMiddleware
    {
        private readonly ApplicationDbContext database;

        public LFSAuthenticationMiddleware(ApplicationDbContext database)
        {
            this.database = database;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            // Check Authorization header for lfs token in "Bearer TOKEN" format

            // If the token is incorrect we'll want to fail with 403 to not cause infinite retries in LFS clients

            await next.Invoke(context);
        }
    }
}
