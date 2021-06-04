namespace ThriveDevCenter.Server.Services
{
    using AspNetCoreRateLimit;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Options;

    public class CustomRateLimitConfiguration : RateLimitConfiguration
    {
        public CustomRateLimitConfiguration(IOptions<IpRateLimitOptions> ipOptions,
            IOptions<ClientRateLimitOptions> clientOptions) : base(ipOptions, clientOptions)
        {
        }

        public override void RegisterResolvers()
        {
            base.RegisterResolvers();

            // TODO: check the request context to get the user id for per-user limiting?

            IpResolvers.Add(new IpConnectionResolveContributor());
        }
    }
}
