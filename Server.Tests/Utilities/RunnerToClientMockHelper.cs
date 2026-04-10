namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Models;
using Common.Services;
using Server.Models;
using Server.Utilities;
using Shared.Models;
using Shared.Models.Enums;
using SharedBase.Utilities;
using Xunit;
using JsonSerializer = System.Text.Json.JsonSerializer;

public class RunnerToClientMockHelper : IRunnerClientCommunication
{
    private readonly List<CiJob> jobs = new();

    private readonly ConcurrentQueue<RealTimeBuildMessage> messagesToClient = new();
    private readonly ConcurrentQueue<RealTimeBuildMessage> messagesToServer = new();

    private readonly Dictionary<CiJob, List<JobOutputSection>> jobOutputSections = new();
    private readonly Dictionary<CiJob, bool> finishedJobStatuses = new();

    private bool serverBrokeConnection;

    private int connectionId = -1;

    private CiJob? activeJob;
    private JobOutputSection? activeSection;

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

            if (!IsConnected)
                throw new OperationCanceledException("Client disconnected");

            await Task.Delay(1);
        }

        throw new TimeoutException("Timed out waiting for client message");
    }

    /// <summary>
    ///   Handles the connection opening and authentication flow
    /// </summary>
    public async Task PerformConnectionAuth()
    {
        // Request auth like the main connection handler
        SendToClient(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.AuthDemand,
        });

        // And then wait for the response
        var authResponse = await WaitForClientMessage();

        Assert.Equal(BuildSectionMessageType.AuthResponse, authResponse.Type);
        Assert.NotNull(authResponse.Output);

        if (RunnerData.SecretKey.ToString() != authResponse.Output)
        {
            SendToClient(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.Error,
                ErrorMessage = "Invalid secret",
            });

            await Task.Delay(5);

            serverBrokeConnection = true;
            return;
        }

        SendToClient(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.AuthSuccess,
        });

        connectionId = new Random().Next();
        IsAuthenticated = true;
    }

    public void SendJobsToClient()
    {
        // Emulate real server in not allowing to start a job (by getting a list of them first)
        // if there is already one running
        if (activeJob != null)
        {
            SendToClient(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.ActiveJobDetails,
                Output = JsonSerializer.Serialize(new RunningJobDetails(activeJob.GetDTO())
                {
                    CacheConfiguration =
                        JsonSerializer.Deserialize<CiJobCacheConfigurationEnriched>(activeJob.CacheSettingsJson ??
                            throw new Exception("Job has no cache settings")) ?? throw new NullDecodedJsonException(),
                }),
            });

            return;
        }

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

    public void CloseServer()
    {
        serverBrokeConnection = true;
    }

    public IRunnerClientDataService GetDataForClient()
    {
        return new RunnerClientDataServiceObjet
        {
            ConnectionKey = RunnerData.AccessId.ToString(),
            SecretKey = RunnerData.SecretKey.ToString(),
            ServerUrl = "dummy.unittest.example.com",
            MaxCacheSize = 1000,
        };
    }

    public void AddJob(CiJob jobToAdd)
    {
        // This ID check is a bit more strict than necessary but is written this way for simplicity
        if (jobs.Contains(jobToAdd) || jobs.Any(j => j.CiJobId == jobToAdd.CiJobId))
            throw new InvalidOperationException("Job already exists");

        jobs.Add(jobToAdd);
    }

    public async Task HandleMessagesUntilJobFinished()
    {
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Wait until client reports a job complete
        while (true)
        {
            if (TryGetFromClient(out var message))
            {
                switch (message.Type)
                {
                    case BuildSectionMessageType.HeartBeat:
                        break;

                    case BuildSectionMessageType.BuildOutput:
                    {
                        Assert.NotNull(activeSection);
                        Assert.NotNull(activeJob);

                        Assert.NotNull(message.Output);

                        if (message.Output.Length > 10000)
                            Assert.Fail("Way too long message generated by client!");

                        activeSection.Text.Append(message.Output);

                        break;
                    }

                    case BuildSectionMessageType.SectionStart:
                    {
                        Assert.Null(activeSection);
                        Assert.NotNull(activeJob);

                        Assert.NotNull(message.SectionName);
                        Assert.True(message.SectionId > 0);

                        activeSection = new JobOutputSection(message.SectionName, message.SectionId);

                        if (!jobOutputSections.TryGetValue(activeJob, out var sections))
                        {
                            sections = new List<JobOutputSection>();
                            jobOutputSections.Add(activeJob, sections);
                        }

                        sections.Add(activeSection);
                        break;
                    }

                    case BuildSectionMessageType.SectionEnd:
                    {
                        Assert.NotNull(activeSection);
                        activeSection.Closed = true;
                        activeSection.Success = message.WasSuccessful;
                        activeSection = null;

                        break;
                    }

                    case BuildSectionMessageType.FinalStatus:
                    {
                        Assert.Null(activeSection);
                        Assert.NotNull(activeJob);

                        finishedJobStatuses.Add(activeJob, message.WasSuccessful);
                        activeJob.Succeeded = message.WasSuccessful;
                        activeJob.State = CIJobState.Finished;
                        activeJob = null;
                        return;
                    }

                    default:
                        Assert.Fail("Unexpected message type: " + message.Type);
                        break;
                }
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    public async Task WaitForClientToStartJob(CiJob jobToAllow)
    {
        if (activeJob != null)
            throw new InvalidOperationException("Already has an active job");

        var cacheForClient =
            JsonSerializer.Deserialize<CiJobCacheConfigurationEnriched>(jobToAllow.CacheSettingsJson ??
                throw new Exception("Job has no cache settings")) ?? throw new NullDecodedJsonException();

        // If the job was not advertised to the runner, it cannot request to start it
        if (!jobs.Contains(jobToAllow))
            throw new InvalidOperationException("Job not registered");

        if (jobToAllow.State is CIJobState.Finished or CIJobState.Running)
            throw new InvalidOperationException("Job is already finished or running");

        if (jobs.IndexOf(jobToAllow) != 0)
            throw new Exception("Job is not at the start of the list, client may not try to start it");

        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Wait for the client to request to start the job
        while (true)
        {
            if (TryGetFromClient(out var message))
            {
                switch (message.Type)
                {
                    case BuildSectionMessageType.HeartBeat:
                        break;
                    case BuildSectionMessageType.GetAvailableJobs:
                        SendJobsToClient();
                        break;
                    case BuildSectionMessageType.RequestStartJob:
                    {
                        // Process the start request
                        Assert.NotNull(message.Output);

                        var data = message.Output.Split(':', 3);

                        var projectId = long.Parse(data[0]);
                        var buildId = long.Parse(data[1]);
                        var jobId = long.Parse(data[2]);

                        // It must match the allowed job
                        Assert.Equal(jobToAllow.CiProjectId, projectId);
                        Assert.Equal(jobToAllow.CiBuildId, buildId);
                        Assert.Equal(jobToAllow.CiJobId, jobId);

                        SendToClient(new RealTimeBuildMessage
                        {
                            Type = BuildSectionMessageType.ActiveJobDetails,
                            Output = JsonSerializer.Serialize(new RunningJobDetails(jobToAllow.GetDTO())
                            {
                                CacheConfiguration = cacheForClient,
                            }),
                        });

                        // Mark the job as being in progress
                        jobToAllow.State = CIJobState.Running;
                        jobToAllow.ReservedByRunnerId = RunnerData.Id;
                        jobToAllow.ReservedByRunner = RunnerData;
                        jobToAllow.OutputConnection = connectionId;

                        activeJob = jobToAllow;
                        return;
                    }

                    case BuildSectionMessageType.BuildOutput:
                    {
                        Assert.NotNull(activeSection);
                        Assert.NotNull(activeJob);

                        Assert.NotNull(message.Output);

                        if (message.Output.Length > 10000)
                            Assert.Fail("Way too long message generated by client!");

                        activeSection.Text.Append(message.Output);

                        break;
                    }

                    case BuildSectionMessageType.SectionStart:
                    {
                        Assert.Null(activeSection);
                        Assert.NotNull(activeJob);

                        Assert.NotNull(message.SectionName);
                        Assert.True(message.SectionId > 0);

                        activeSection = new JobOutputSection(message.SectionName, message.SectionId);

                        if (!jobOutputSections.TryGetValue(activeJob, out var sections))
                        {
                            sections = new List<JobOutputSection>();
                            jobOutputSections.Add(activeJob, sections);
                        }

                        sections.Add(activeSection);
                        break;
                    }

                    case BuildSectionMessageType.SectionEnd:
                    {
                        Assert.NotNull(activeSection);
                        activeSection.Closed = true;
                        activeSection.Success = message.WasSuccessful;
                        activeSection = null;

                        break;
                    }

                    case BuildSectionMessageType.FinalStatus:
                    {
                        Assert.Null(activeSection);
                        Assert.NotNull(activeJob);

                        finishedJobStatuses.Add(activeJob, message.WasSuccessful);
                        activeJob.Succeeded = message.WasSuccessful;
                        activeJob.State = CIJobState.Finished;
                        activeJob = null;
                        break;
                    }

                    default:
                        Assert.Fail("Unexpected message type: " + message.Type);
                        break;
                }
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    public bool? GetJobFinishedStatus(CiJob job)
    {
        if (!finishedJobStatuses.TryGetValue(job, out var status))
            return null;

        return status;
    }

    public List<JobOutputSection>? GetJobOutputSections(CiJob job)
    {
        return jobOutputSections.GetValueOrDefault(job);
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
        if (!IsConnected)
            throw new InvalidOperationException("Socket is not connected");

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

        var endBy = DateTime.UtcNow + timeout;

        while (true)
        {
            if (messagesToClient.TryDequeue(out var message))
            {
                return message;
            }

            if (DateTime.UtcNow > endBy)
                return null;

            // We use just this immediate cancellation and not the client-provided one as in the tests we will time out
            // the entire test instead of a single read
            await Task.Delay(1, cancellationToken);
        }
    }

    public class JobOutputSection(string name, long id)
    {
        public bool Closed { get; set; }
        public bool Success { get; set; }
        public string Name { get; set; } = name;
        public long Id { get; set; } = id;
        public StringBuilder Text { get; set; } = new();
    }
}
