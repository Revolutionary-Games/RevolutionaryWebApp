namespace RevolutionaryWebApp.Server.Utilities;

using System.Threading.Tasks;
using Common.Utilities;
using Microsoft.AspNetCore.Http;

public class HttpContextMessageSocketFactory(HttpContext context) : IBuildMessageSocketFactory
{
    public async Task<IRealTimeBuildMessageSocket> AcceptAsync()
    {
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();

        return new RealTimeBuildMessageSocket(webSocket);
    }
}
