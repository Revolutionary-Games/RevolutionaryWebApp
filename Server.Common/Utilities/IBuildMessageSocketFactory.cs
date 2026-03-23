namespace RevolutionaryWebApp.Server.Common.Utilities;

using System.Threading.Tasks;

public interface IBuildMessageSocketFactory
{
    public Task<IRealTimeBuildMessageSocket> AcceptAsync();
}
