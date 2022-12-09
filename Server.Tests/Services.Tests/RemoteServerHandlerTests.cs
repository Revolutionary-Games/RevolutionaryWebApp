namespace ThriveDevCenter.Server.Tests.Services.Tests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon.EC2.Model;
using Fixtures;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
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
        new List<KeyValuePair<string, string>>
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
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.LaunchNewInstance())
            .Returns(Task.FromResult(new List<string> { NewInstanceId })).Verifiable();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersNewInstancesCreate")
            .Options, notificationsMock.Object);

        CIProjectTestDatabaseData.Seed(database);
        var job = await AddTestJob(database);

        var handler =
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

        Assert.Empty(database.ControlledServers);
        Assert.False(handler.NewServersAdded);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.True(handler.NewServersAdded);

        Assert.NotEmpty(database.ControlledServers);

        var server = await database.ControlledServers.FirstOrDefaultAsync();

        Assert.NotNull(server);
        Assert.Equal(NewInstanceId, server.InstanceId);
        Assert.Equal(ServerStatus.Provisioning, server.Status);

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_RightNumberOfExistingInstancesAreStarted()
    {
        const string instanceId1 = "id-1111";

        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.ResumeInstance(instanceId1)).Returns(Task.CompletedTask).Verifiable();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersStartExisting")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(ServerStatus.WaitingForStartup, server1.Status);

        Assert.NotEqual(ServerStatus.WaitingForStartup, server2.Status);

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_RunningServerIsUsedForJobs()
    {
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.ResumeInstance(It.IsAny<string>())).Throws<InvalidOperationException>();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersRunOnExisting")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(ServerStatus.Stopped, server1.Status);
        Assert.Equal(ServerReservationType.None, server1.ReservationType);
        Assert.Null(server1.ReservedFor);

        Assert.Equal(ServerStatus.Running, server2.Status);
        Assert.Equal(ServerReservationType.CIJob, server2.ReservationType);
        Assert.Equal(job.CiJobId, server2.ReservedFor);

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_JobIsNotStartedOnMultipleServers()
    {
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.ResumeInstance(It.IsAny<string>())).Throws<InvalidOperationException>();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersJobNotStartedOnMultiple")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

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

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_StartingServerIsDetectedAsStarted()
    {
        const string instanceId1 = "id-1111";

        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 =>
                ec2.GetInstanceStatuses(new List<string> { instanceId1 }, It.IsAny<CancellationToken>()))
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
            }))
            .Verifiable();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersDetectRunning")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

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

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_IdleServersAreStopped()
    {
        const string instanceId1 = "id-1111";

        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.StopInstance(instanceId1, false)).Returns(Task.CompletedTask).Verifiable();
        ec2Mock.Setup(ec2 =>
                ec2.GetInstanceStatuses(new List<string> { instanceId1 }, It.IsAny<CancellationToken>()))
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
            }))
            .Verifiable();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersIdleStop")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

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

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_TotalServerLimitIsRespectedWhenCreating()
    {
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.LaunchNewInstance()).Throws<InvalidOperationException>();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersNewInstancesFull")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(3, await database.ControlledServers.CountAsync());
        Assert.Equal(123, server1.ReservedFor);
        Assert.Equal(123, server2.ReservedFor);
        Assert.Equal(123, server3.ReservedFor);

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_ExtraProvisioningServersAreNotStarted()
    {
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.LaunchNewInstance()).Throws<InvalidOperationException>();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ControlledServersProvisionExistingCount")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.False(handler.NewServersAdded);

        Assert.Equal(2, await database.ControlledServers.CountAsync());
        Assert.Equal(123, server1.ReservedFor);
        Assert.Null(server2.ReservedFor);
        Assert.Equal(ServerStatus.Provisioning, server2.Status);

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_ExternalServersAreUsedBeforeEC2()
    {
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(true);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ExternalServersBeforeEC2")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

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

        ec2Mock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ServerControl_EC2NotBeingConfiguredAllowsExternalToRun()
    {
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(false).Verifiable();

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ExternalRunEC2NotConfiguredForMore")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

        Assert.True(await handler.HandleCIJobs(new List<CiJob> { job }));

        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(job.CiJobId, server1.ReservedFor);

        Assert.False(await handler.HandleCIJobs(new List<CiJob> { job2 }));

        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(job.CiJobId, server1.ReservedFor);

        ec2Mock.Verify();
    }

    [Fact]
    public async Task ServerControl_ExternalServersPriorityWorks()
    {
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.SetupGet(ec2 => ec2.Configured).Returns(false);

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ExternalServerPriority")
            .Options, notificationsMock.Object);

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
            new RemoteServerHandler(logger, configuration, database, ec2Mock.Object, jobClientMock.Object);

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

        ec2Mock.VerifyNoOtherCalls();
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
