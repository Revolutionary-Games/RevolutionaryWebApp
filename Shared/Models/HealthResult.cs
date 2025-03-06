namespace RevolutionaryWebApp.Shared.Models;

using System.ComponentModel.DataAnnotations;

public class HealthResult
{
    public HealthResult(string status, string server)
    {
        Status = status;
        Server = server;
    }

    [Required]
    public string Status { get; set; }

    public bool IsHealthy { get; set; }

    [Required]
    public string Server { get; set; }

    /// <summary>
    ///   Uptime in seconds of the server
    /// </summary>
    public float Uptime { get; set; }

    public float? TimeSinceError { get; set; }
}
