namespace ThriveDevCenter.Server.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public interface IRegistrationStatus
{
    public bool RegistrationEnabled { get; }
    public string? RegistrationCode { get; }
}

public class RegistrationStatus : IRegistrationStatus
{
    public RegistrationStatus(IConfiguration configuration, ILogger<RegistrationStatus> logger)
    {
        RegistrationCode = configuration.GetValue("Registration:RegistrationCode", string.Empty);

        if (configuration.GetValue("Registration:Enabled", false) && !string.IsNullOrEmpty(RegistrationCode))
        {
            logger.LogInformation("Registration is enabled on this instance");
            RegistrationEnabled = true;
        }
        else
        {
            RegistrationEnabled = false;
        }
    }

    public bool RegistrationEnabled { get; }
    public string RegistrationCode { get; }
}
