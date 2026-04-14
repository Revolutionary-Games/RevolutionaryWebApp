namespace CIExecutor;

using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RevolutionaryWebApp.Server.Common.Services;
using RevolutionaryWebApp.Server.Common.Utilities;
using RevolutionaryWebApp.Shared.Models;

/// <summary>
///   Implements runner-to-server communication via websockets
/// </summary>
public sealed class RunnerClientWebsocket : IRunnerClientCommunication, IDisposable
{
    private readonly ILogger logger;
    private readonly string serverUrlEndpoint;

    private ClientWebSocket? webSocket;
    private RealTimeBuildMessageSocket? protocolSocket;

    public RunnerClientWebsocket(ILogger logger, string serverUrlEndpoint, IRunnerClientDataService dataService)
    {
        this.logger = logger;
        this.serverUrlEndpoint = serverUrlEndpoint;

        // Quiet my IDE
        var httpWorkaround = "ht" + "tp://";

        // Convert HTTP to WebSocket URLs
        this.serverUrlEndpoint = serverUrlEndpoint.Replace("https://", "wss://").Replace(httpWorkaround, "ws://");

        // Add the access key to the URL so that we can connect
        this.serverUrlEndpoint += "?runnerId=" + dataService.ConnectionKey;
    }

    public bool IsConnected { get; private set; }
    public int LatestErrorCount { get; private set; }

    public async Task Connect(CancellationToken cancellationToken)
    {
        if (webSocket != null)
        {
            try
            {
                logger.LogWarning("Websocket already created, will disconnect");
                await Close();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to close existing websocket");
            }
        }

        webSocket = new ClientWebSocket();

        // Hopefully this is low enough to not cause issues with disconnects
        webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(45);

        try
        {
            await webSocket.ConnectAsync(new Uri(serverUrlEndpoint), cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to connect to websocket");
            throw;
        }

        logger.LogInformation("Websocket to server opened");

        protocolSocket = new RealTimeBuildMessageSocket(webSocket);

        logger.LogInformation("Created custom protocol socket");
        IsConnected = true;
        LatestErrorCount = 0;
    }

    public async Task Close()
    {
        if (webSocket == null)
        {
            logger.LogInformation("Websocket already closed");
            protocolSocket = null;
            return;
        }

        logger.LogInformation("Closing websocket...");

        try
        {
            IsConnected = false;
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing",
                new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

            logger.LogInformation("Socket closed successfully");
            webSocket.Dispose();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to close websocket");
        }

        webSocket = null;
        protocolSocket = null;
    }

    public async Task Send(RealTimeBuildMessage message, CancellationToken cancellationToken)
    {
        if (protocolSocket == null || !IsConnected)
            throw new InvalidOperationException("Socket is not connected");

        try
        {
            await protocolSocket.Write(message, cancellationToken);

            LatestErrorCount = 0;
        }
        catch (WebSocketProtocolException e)
        {
            logger.LogWarning(e, "Socket write exception, inner: {Inner}", e.InnerException);
            ++LatestErrorCount;
            IsConnected = false;

            throw new InvalidOperationException("Socket has closed / did not manage to send", e);
        }
    }

    public async Task<RealTimeBuildMessage?> Receive(CancellationToken cancellationToken)
    {
        if (protocolSocket == null || !IsConnected)
            throw new InvalidOperationException("Socket is not connected");

        try
        {
            var (message, closed) = await protocolSocket.Read(cancellationToken);

            if (closed)
            {
                logger.LogInformation("Socket has become closed on read");
                IsConnected = false;
                return null;
            }

            LatestErrorCount = 0;
            return message;
        }
        catch (OperationCanceledException)
        {
            // The websocket dies when cancelling the read...
            IsConnected = false;
            throw;
        }
        catch (WebSocketProtocolException e)
        {
            logger.LogWarning(e, "Socket read exception, inner: {Inner}", e.InnerException);
            ++LatestErrorCount;
            IsConnected = false;

            throw new InvalidOperationException("Socket has closed", e);
        }
    }

    public void Dispose()
    {
        IsConnected = false;
        try
        {
            webSocket?.Dispose();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to dispose websocket on Dispose");
        }

        webSocket = null;
        protocolSocket = null;
    }
}
