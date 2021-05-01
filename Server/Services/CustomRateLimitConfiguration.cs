namespace ThriveDevCenter.Server.Services
{
    using AspNetCoreRateLimit;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Options;

    public class CustomRateLimitConfiguration : RateLimitConfiguration
    {
        public CustomRateLimitConfiguration(IHttpContextAccessor httpContextAccessor,
            IOptions<IpRateLimitOptions> ipOptions, IOptions<ClientRateLimitOptions> clientOptions) : base(
            httpContextAccessor, ipOptions, clientOptions)
        {
        }

        protected override void RegisterResolvers()
        {
            base.RegisterResolvers();

            // TODO: check the request context to get the user id for per-user limiting?

            IpResolvers.Add(new IpConnectionResolveContributor(HttpContextAccessor));
        }
    }
}
