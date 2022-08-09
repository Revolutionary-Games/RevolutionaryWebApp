namespace ThriveDevCenter.Server.Tests.Dummies;

using Server.Services;

public class DummyRegistrationStatus : IRegistrationStatus
{
    public bool RegistrationEnabled { get; set; }
    public string RegistrationCode { get; set; } = string.Empty;
}