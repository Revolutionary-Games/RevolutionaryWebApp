namespace ThriveDevCenter.Server.Utilities
{
    using Microsoft.AspNetCore.Hosting;

    public static class WebHostEnvironmentHelpers
    {
        public static bool IsTesting(this IWebHostEnvironment env)
        {
            return env.EnvironmentName == "Testing";
        }
    }
}
