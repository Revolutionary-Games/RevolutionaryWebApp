namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using Microsoft.Extensions.Configuration;

    public static class ConfigurationHelpers
    {
        public static Uri GetBaseUrl(this IConfiguration configuration)
        {
            return new Uri(configuration["BaseUrl"]);
        }
    }
}
