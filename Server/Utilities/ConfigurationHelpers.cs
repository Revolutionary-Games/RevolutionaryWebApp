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

        /// <summary>
        ///   Returns a base url relative full url. TODO: if relative part is full url it should take precedence
        /// </summary>
        /// <param name="configuration">The configuration to read from</param>
        /// <param name="key">The configuration key to read the relative part from</param>
        /// <returns>The full url</returns>
        public static Uri BaseUrlRelative(this IConfiguration configuration, string key)
        {
            return new Uri(configuration.GetBaseUrl(), configuration[key]);
        }
    }
}
