namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Models;
using Common.Services;
using Server.Models;
using Server.Utilities;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;
using JsonSerializer = System.Text.Json.JsonSerializer;

public class RunnerToClientMockHelper : IRunnerClientCommunication
{
    private readonly List<CiJob> jobs = new();

    private readonly ConcurrentQueue<RealTimeBuildMessage> messagesToClient = new();
    private readonly ConcurrentQueue<RealTimeBuildMessage> messagesToServer = new();

    private bool serverBrokeConnection = false;

    public RunnerToClientMockHelper()
    {
        RunnerData = new RemoteRunner("Runner 1")
        {
            Id = 1,
            AccessId = Guid.NewGuid(),
            SecretKey = Guid.NewGuid(),
        };

        RunnerData.HashedAccessId = SelectByHashedProperty.HashForDatabaseValue(RunnerData.SecretKey.ToString());
    }

    public bool IsConnected { get; set; }
    public int LatestErrorCount { get; set; }

    public bool IsAuthenticated { get; set; }

    public RemoteRunner RunnerData { get; }

    public void SendToClient(RealTimeBuildMessage message)
    {
        messagesToClient.Enqueue(message);

        if (messagesToClient.Count > 1000)
            throw new InvalidOperationException("Too many messages in the queue");
    }

    public bool TryGetFromClient([NotNullWhen(true)] out RealTimeBuildMessage? message)
    {
        return messagesToServer.TryDequeue(out message);
    }

    public async Task<RealTimeBuildMessage> WaitForClientMessage(TimeSpan timeout = default)
    {
        if (timeout == TimeSpan.Zero)
            timeout = TimeSpan.FromSeconds(15);

        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (messagesToServer.TryDequeue(out var message))
                return message;

            await Task.Delay(1);
        }

        throw new TimeoutException("Timed out waiting for client message");
    }

    /// <summary>
    ///   Handles the connection opening and authentication flow
    /// </summary>
    public async Task PerformConnectionAuth()
    {
        throw new NotImplementedException();

        var authResponse = await WaitForClientMessage();

        Assert.Equal(BuildSectionMessageType.AuthResponse, authResponse.Type);
        Assert.NotNull(authResponse.Output);

        IsAuthenticated = true;
    }

    public void SendJobsToClient()
    {
        SendToClient(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.JobsList,
            Output = JsonSerializer.Serialize(new AvailableJobsList
            {
                FilteredCount = 0,
                Jobs = jobs.Select(j => j.GetDTO()).ToList(),
            }),
        });
    }

    // -
    // Only client allowed methods, don't call these from tests directly!
    // -
    public Task Connect(CancellationToken cancellationToken)
    {
        if (IsConnected)
            throw new InvalidOperationException("Already connected");

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task Close()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task Send(RealTimeBuildMessage message, CancellationToken cancellationToken)
    {
        messagesToServer.Enqueue(message);

        if (messagesToServer.Count > 1000)
            throw new InvalidOperationException("Too many messages in the queue");

        return Task.CompletedTask;
    }

    public async Task<RealTimeBuildMessage?> Receive(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (serverBrokeConnection)
        {
            IsConnected = false;
            return null;
        }

        while (true)
        {
            if (messagesToClient.TryDequeue(out var message))
            {
                return message;
            }

            // We use just this immediate cancellation and not the client-provided one as in the tests we will time out
            // the entire test instead of a single read
            await Task.Delay(1, cancellationToken);
        }
    }
}
