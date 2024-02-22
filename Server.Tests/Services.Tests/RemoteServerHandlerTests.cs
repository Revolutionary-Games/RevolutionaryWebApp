namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon.EC2.Model;
using Fixtures;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Models;
using Server.Services;
using Shared.Models;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class RemoteServerHandlerTests : IDisposable
{
    private const string NewInstanceId = "id-1231245";

    private readonly XunitLogger<RemoteServerHandler> logger;

    private readonly IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
        new List<KeyValuePair<string, string?>>
        {
            new("CI:ServerIdleTimeBeforeStop", "60"),
            new("CI:MaximumConcurrentServers", "3"),
            new("CI:UseHibernate", "false"),
        }).Build();

    public RemoteServerHandlerTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<RemoteServerHandler>(output);
    }

    [Fact]
    public async Task ServerControl_NewInstancesAreCreated()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.LaunchNewInstance().Returns(Task.FromResult(new List<string> { NewInstanceId }));
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersNewInstancesCreate")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.Empty(database.ControlledServers);
        Assert.False(handler.NewServersAdded);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.True(handler.NewServersAdded);

        Assert.NotEmpty(database.ControlledServers);

        var server = await database.ControlledServers.FirstOrDefaultAsync();

        Assert.NotNull(server);
        Assert.Equal(NewInstanceId, server.InstanceId);
        Assert.Equal(ServerStatus.Provisioning, server.Status);

        await ec2Mock.Received().LaunchNewInstance();
    }

    [Fact]
    public async Task ServerControl_RightNumberOfExistingInstancesAreStarted()
    {
        const string instanceId1 = "id-1111";

        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.ResumeInstance(instanceId1).Returns(Task.CompletedTask);
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersStartExisting")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var server1 = new ControlledServer
        {
            Status = ServerStatus.Stopped,
            ProvisionedFully = true,
            InstanceId = instanceId1,
        };

        await database.ControlledServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Stopped,
            ProvisionedFully = true,
            InstanceId = "id-2222",
        };

        await database.ControlledServers.AddAsync(server2);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(ServerStatus.WaitingForStartup, server1.Status);

        Assert.NotEqual(ServerStatus.WaitingForStartup, server2.Status);

        await ec2Mock.Received().ResumeInstance(instanceId1);
    }

    [Fact]
    public async Task ServerControl_RunningServerIsUsedForJobs()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersRunOnExisting")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var server1 = new ControlledServer
        {
            Status = ServerStatus.Stopped,
            ProvisionedFully = true,
            InstanceId = "id-1111",
        };

        await database.ControlledServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-2222",
        };

        await database.ControlledServers.AddAsync(server2);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(ServerStatus.Stopped, server1.Status);
        Assert.Equal(ServerReservationType.None, server1.ReservationType);
        Assert.Null(server1.ReservedFor);

        Assert.Equal(ServerStatus.Running, server2.Status);
        Assert.Equal(ServerReservationType.CIJob, server2.ReservationType);
        Assert.Equal(job.CiJobId, server2.ReservedFor);

        await ec2Mock.DidNotReceiveWithAnyArgs().ResumeInstance(Arg.Any<string>());
    }

    [Fact]
    public async Task ServerControl_JobIsNotStartedOnMultipleServers()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersJobNotStartedOnMultiple")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var server1 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-1111",
        };

        await database.ControlledServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-2222",
        };

        await database.ControlledServers.AddAsync(server2);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.NotNull(server1.ReservedFor);
        Assert.Null(server2.ReservedFor);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.NotNull(server1.ReservedFor);
        Assert.Null(server2.ReservedFor);
        Assert.Equal(CIJobState.WaitingForServer, job.State);

        Assert.Equal(ServerStatus.Running, server1.Status);
        Assert.Equal(ServerStatus.Running, server2.Status);
        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(ServerReservationType.None, server2.ReservationType);

        await ec2Mock.DidNotReceiveWithAnyArgs().ResumeInstance(Arg.Any<string>());
    }

    [Fact]
    public async Task ServerControl_StartingServerIsDetectedAsStarted()
    {
        const string instanceId1 = "id-1111";

        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.GetInstanceStatuses(Arg.Is<List<string>>(l => l.SequenceEqual(new List<string> { instanceId1 })),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<Instance>
            {
                new()
                {
                    InstanceId = instanceId1,
                    PublicIpAddress = "1.2.3.4",
                    State = new InstanceState
                    {
                        Code = 123,
                        Name = InstanceStateName.Running,
                    },
                },
            }));
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersDetectRunning")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var server1 = new ControlledServer
        {
            Status = ServerStatus.WaitingForStartup,
            ProvisionedFully = true,
            InstanceId = instanceId1,
            StatusLastChecked = DateTime.UtcNow - TimeSpan.FromSeconds(60),
        };

        await database.ControlledServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Stopped,
            ProvisionedFully = true,
            InstanceId = "id-2222",
            StatusLastChecked = DateTime.UtcNow - TimeSpan.FromSeconds(60),
        };

        await database.ControlledServers.AddAsync(server2);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.Equal(ServerStatus.WaitingForStartup, server1.Status);
        await handler.CheckServerStatuses(CancellationToken.None);
        Assert.Equal(ServerStatus.Running, server1.Status);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(ServerStatus.Running, server1.Status);
        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(job.CiJobId, server1.ReservedFor);

        Assert.Equal(ServerStatus.Stopped, server2.Status);
        Assert.Equal(ServerReservationType.None, server2.ReservationType);
        Assert.Null(server2.ReservedFor);

        await ec2Mock.Received()
            .GetInstanceStatuses(Arg.Is<List<string>>(l => l.SequenceEqual(new List<string> { instanceId1 })),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ServerControl_IdleServersAreStopped()
    {
        const string instanceId1 = "id-1111";

        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.StopInstance(instanceId1, false).Returns(Task.CompletedTask);
        ec2Mock.GetInstanceStatuses(Arg.Is<List<string>>(l => l.SequenceEqual(new List<string> { instanceId1 })),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<Instance>
            {
                new()
                {
                    InstanceId = instanceId1,
                    State = new InstanceState
                    {
                        Code = 123,
                        Name = InstanceStateName.Stopped,
                    },
                },
            }));
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersIdleStop")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        await AddTestJob(database);

        var server1 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = instanceId1,
            StatusLastChecked = DateTime.UtcNow - TimeSpan.FromSeconds(60),
            UpdatedAt = DateTime.UtcNow - TimeSpan.FromSeconds(300),
        };

        await database.ControlledServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-2222",
            StatusLastChecked = DateTime.UtcNow - TimeSpan.FromSeconds(60),
        };

        await database.ControlledServers.AddAsync(server2);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.Equal(ServerStatus.Running, server1.Status);
        Assert.Equal(ServerStatus.Running, server2.Status);

        await handler.CheckServerStatuses(CancellationToken.None);

        Assert.Equal(ServerStatus.Running, server1.Status);
        Assert.Equal(ServerStatus.Running, server2.Status);

        await handler.ShutdownIdleServers();

        Assert.Equal(ServerStatus.Stopping, server1.Status);
        Assert.Equal(ServerStatus.Running, server2.Status);

        await handler.CheckServerStatuses(CancellationToken.None);

        Assert.Equal(ServerStatus.Stopped, server1.Status);
        Assert.Equal(ServerStatus.Running, server2.Status);

        await ec2Mock.Received().StopInstance(instanceId1, false);
    }

    [Fact]
    public async Task ServerControl_TotalServerLimitIsRespectedWhenCreating()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersNewInstancesFull")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var server1 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-1111",
            ReservationType = ServerReservationType.CIJob,
            ReservedFor = 123,
        };

        await database.ControlledServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-2222",
            ReservationType = ServerReservationType.CIJob,
            ReservedFor = 123,
        };

        await database.ControlledServers.AddAsync(server2);

        var server3 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-3333",
            ReservationType = ServerReservationType.CIJob,
            ReservedFor = 123,
        };

        await database.ControlledServers.AddAsync(server3);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(3, await database.ControlledServers.CountAsync());
        Assert.Equal(123, server1.ReservedFor);
        Assert.Equal(123, server2.ReservedFor);
        Assert.Equal(123, server3.ReservedFor);

        await ec2Mock.DidNotReceive().LaunchNewInstance();
    }

    [Fact]
    public async Task ServerControl_ExtraProvisioningServersAreNotStarted()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersProvisionExistingCount")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var server1 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-1111",
            ReservationType = ServerReservationType.CIJob,
            ReservedFor = 123,
        };

        await database.ControlledServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Provisioning,
            ProvisionedFully = true,
            InstanceId = "id-2222",
        };

        await database.ControlledServers.AddAsync(server2);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(2, await database.ControlledServers.CountAsync());
        Assert.Equal(123, server1.ReservedFor);
        Assert.Null(server2.ReservedFor);
        Assert.Equal(ServerStatus.Provisioning, server2.Status);

        await ec2Mock.DidNotReceive().LaunchNewInstance();
    }

    [Fact]
    public async Task ServerControl_ExternalServersAreUsedBeforeEC2()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.Configured.Returns(true);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ExternalServersBeforeEC2")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var job2 = new CiJob
        {
            CiProjectId = CIProjectTestDatabaseData.CIProjectId,
            CiBuildId = CIProjectTestDatabaseData.CIBuildId,
            CiJobId = 2,
            JobName = "job2",
            Image = "test/test:v1",
            CacheSettingsJson = "{}",
        };

        await database.CiJobs.AddAsync(job2);

        var server1 = new ExternalServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            PublicAddress = new IPAddress(1234),
            SSHKeyFileName = "key.pem",
        };

        await database.ExternalServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            InstanceId = "id-1111",
        };

        await database.ControlledServers.AddAsync(server2);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(job.CiJobId, server1.ReservedFor);

        Assert.Equal(ServerReservationType.None, server2.ReservationType);
        Assert.Null(server2.ReservedFor);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job2 }));

        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(job.CiJobId, server1.ReservedFor);

        Assert.Equal(ServerReservationType.CIJob, server2.ReservationType);
        Assert.Equal(job2.CiJobId, server2.ReservedFor);

        await ec2Mock.DidNotReceiveWithAnyArgs().LaunchNewInstance();
    }

    [Fact]
    public async Task ServerControl_EC2NotBeingConfiguredAllowsExternalToRun()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.Configured.Returns(false);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ExternalRunEC2NotConfiguredForMore")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var job2 = new CiJob
        {
            CiProjectId = CIProjectTestDatabaseData.CIProjectId,
            CiBuildId = CIProjectTestDatabaseData.CIBuildId,
            CiJobId = 2,
            JobName = "job2",
            Image = "test/test:v1",
            CacheSettingsJson = "{}",
        };

        await database.CiJobs.AddAsync(job2);

        var server1 = new ExternalServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            PublicAddress = new IPAddress(1234),
            SSHKeyFileName = "key.pem",
        };

        await database.ExternalServers.AddAsync(server1);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(job.CiJobId, server1.ReservedFor);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job2 }));

        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(job.CiJobId, server1.ReservedFor);

        _ = ec2Mock.Received().Configured;
    }

    [Fact]
    public async Task ServerControl_ExternalServersPriorityWorks()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.Configured.Returns(false);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ExternalServerPriority")
            .Options, notificationsMock);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var job2 = new CiJob
        {
            CiProjectId = CIProjectTestDatabaseData.CIProjectId,
            CiBuildId = CIProjectTestDatabaseData.CIBuildId,
            CiJobId = 2,
            JobName = "job2",
            Image = "test/test:v1",
            CacheSettingsJson = "{}",
        };

        await database.CiJobs.AddAsync(job2);

        var server1 = new ExternalServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            PublicAddress = new IPAddress(1234),
            SSHKeyFileName = "key.pem",
        };

        await database.ExternalServers.AddAsync(server1);

        var server2 = new ExternalServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            Priority = 1,
            PublicAddress = new IPAddress(5678),
            SSHKeyFileName = "key.pem",
        };

        await database.ExternalServers.AddAsync(server2);
        await database.SaveChangesAsync();

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock, jobClientMock);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.Equal(ServerReservationType.CIJob, server2.ReservationType);
        Assert.Equal(job.CiJobId, server2.ReservedFor);

        Assert.Equal(ServerReservationType.None, server1.ReservationType);
        Assert.Null(server1.ReservedFor);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job2 }));

        Assert.Equal(ServerReservationType.CIJob, server2.ReservationType);
        Assert.Equal(job.CiJobId, server2.ReservedFor);

        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(job2.CiJobId, server1.ReservedFor);

        await ec2Mock.DidNotReceiveWithAnyArgs().LaunchNewInstance();
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static async Task<CiJob> AddTestJob(NotificationsEnabledDb database)
    {
        var job = new CiJob
        {
            CiProjectId = CIProjectTestDatabaseData.CIProjectId,
            CiBuildId = CIProjectTestDatabaseData.CIBuildId,
            CiJobId = 1,
            JobName = "Test job",
            Image = "test/test:v1",
            CacheSettingsJson = "{}",
        };

        await database.CiJobs.AddAsync(job);

        await database.SaveChangesAsync();
        return job;
    }
}
