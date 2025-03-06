namespace RevolutionaryWebApp.Server.Services;

using System;

/// <summary>
///   Singleton for accessing health information for the *current* server
/// </summary>
public class HealthTracker
{
    private readonly DateTime serverStartTime = DateTime.UtcNow;

    public TimeSpan GetUptime()
    {
        return DateTime.UtcNow - serverStartTime;
    }
}
