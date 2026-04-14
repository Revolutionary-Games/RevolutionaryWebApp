namespace RevolutionaryWebApp.Server.Common.Utilities;

using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Shared.Models;

public interface IRealTimeBuildMessageSocket
{
    public WebSocketCloseStatus? CloseStatus { get; }

    public Task<(RealTimeBuildMessage? Message, bool Closed)> Read(CancellationToken cancellationToken);

    public Task Write(RealTimeBuildMessage message, CancellationToken cancellationToken);

    public Task<bool> Close(CancellationToken cancellationToken);
}
