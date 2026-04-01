namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System;
using System.Threading.Tasks;
using Shared.Models;

/// <summary>
///   Moves data between <see cref="RunnerConnectionMockHelper"/> and <see cref="RunnerToClientMockHelper"/>
/// </summary>
public class RunnerClientAndServerMockBridge
{
    private readonly RunnerConnectionMockHelper server;
    private readonly RunnerToClientMockHelper client;

    public RunnerClientAndServerMockBridge(RunnerConnectionMockHelper server, RunnerToClientMockHelper client)
    {
        this.server = server;
        this.client = client;
    }

    public async Task RunBridge()
    {
        // Already need to proxy to the messages so that connection manages to open
        var clientTask = RunClientMessageLoop();
        var serverTask = RunServerMessageLoop();

        // Then start the server
        await server.Start(false);

        // TODO: we probably want to close once it's been a second without any messages?
        /*await Task.Delay(TimeSpan.FromSeconds(1));
        server.QueueCloseMessage();*/

        // After test, wait until over
        await Task.WhenAll(serverTask, clientTask);
        await server.WaitUntilClosed();
    }

    private async Task RunServerMessageLoop()
    {
        while (server.IsConnectionOpen())
        {
            var message = await server.WaitForServerMessage();

            if (message == null)
            {
                client.CloseServer();
                break;
            }

            client.SendToClient(message);
        }
    }

    private async Task RunClientMessageLoop()
    {
        while (client.IsConnected)
        {
            RealTimeBuildMessage message;
            try
            {
                message = await client.WaitForClientMessage();
            }
            catch (OperationCanceledException)
            {
                server.QueueCloseMessage();
                break;
            }

            server.QueueMessage(message, false);
        }
    }
}
