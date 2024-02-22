namespace RevolutionaryWebApp.Shared.Models;

public enum ServerStatus
{
    Provisioning,
    WaitingForStartup,
    Running,
    Stopping,
    Stopped,
    Terminated,
}
