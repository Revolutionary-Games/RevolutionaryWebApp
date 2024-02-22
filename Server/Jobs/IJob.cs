namespace RevolutionaryWebApp.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;

public interface IJob
{
    public Task Execute(CancellationToken cancellationToken);
}
