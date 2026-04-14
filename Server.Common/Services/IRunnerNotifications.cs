namespace RevolutionaryWebApp.Server.Common.Services;

using System.Threading.Tasks;

public interface IRunnerNotifications
{
    public Task ReceiveNewJobNotice();
}
