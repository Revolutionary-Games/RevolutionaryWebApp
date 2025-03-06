namespace RevolutionaryWebApp.Server.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Services;
using Shared.Models;

[ApiController]
[Route("api/v1/[controller]")]
public class HealthController : Controller
{
    private readonly HealthTracker healthTracker;
    private readonly string serverName;

    public HealthController(IConfiguration configuration, HealthTracker healthTracker)
    {
        this.healthTracker = healthTracker;

        var name = configuration["ServerName"];

        serverName = string.IsNullOrEmpty(name) ? "server" : name;
    }

    [HttpGet]
    public ActionResult<HealthResult> Index()
    {
        // TODO: implement tracking of errors on the server side

        return new HealthResult("healthy", serverName)
        {
            IsHealthy = true,
            TimeSinceError = null,
            Uptime = (float)healthTracker.GetUptime().TotalSeconds,
        };
    }
}
