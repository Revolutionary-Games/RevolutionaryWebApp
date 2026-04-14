namespace RevolutionaryWebApp.Server.Common.Services;

using System.Threading;
using System.Threading.Tasks;
using Shared.Models;

public interface IRunnerClientCommunication
{
    /// <summary>
    ///   True when successfully connected to the backend
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    ///   Keeps track of the reconnection count since the last successful connection.
    /// </summary>
    public int LatestErrorCount { get; }

    /// <summary>
    ///   Connects to the backend. Throws on error.
    /// </summary>
    /// <returns>Task</returns>
    public Task Connect(CancellationToken cancellationToken);

    public Task Close();

    /// <summary>
    ///   Sends a message to the backend. Will automatically reconnect if the connection is lost.
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <param name="cancellationToken">To cancel the send operation</param>
    /// <returns>Task</returns>
    public Task Send(RealTimeBuildMessage message, CancellationToken cancellationToken);

    public Task<RealTimeBuildMessage?> Receive(CancellationToken cancellationToken);
}
