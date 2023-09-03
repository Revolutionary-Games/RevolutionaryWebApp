namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Hangfire;
using NSubstitute;
using Server.Jobs;
using Server.Models;
using Server.Services;
using Shared.Models;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class RunMarkedServerMaintenanceJobTests : IDisposable
{
    private readonly XunitLogger<RunMarkedServerMaintenanceJob> logger;

    public RunMarkedServerMaintenanceJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<RunMarkedServerMaintenanceJob>(output);
    }

    [Fact]
    public async Task MaintenanceJobTests_IgnoresReservedServer()
    {
        const string secondServerInstance = "5678";

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var database = new EditableInMemoryDatabaseFixtureWithNotifications(notificationsMock,
            "MarkedServerMaintenanceIgnoreReserved");

        var jobClientMock = Substitute.For<IBackgroundJobClient>();
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.TerminateInstance(secondServerInstance).Returns(Task.CompletedTask);

        var sshMock = Substitute.For<IExternalServerSSHAccess>();

        var server1 = new ControlledServer
        {
            Id = 1,
            InstanceId = "1234",
            Status = ServerStatus.Running,
            ReservationType = ServerReservationType.CIJob,
            ReservedFor = 1,
            WantsMaintenance = true,
        };

        var server2 = new ControlledServer
        {
            Id = 2,
            InstanceId = secondServerInstance,
            Status = ServerStatus.Stopped,
            WantsMaintenance = true,
        };

        await database.Database.ControlledServers.AddAsync(server1);
        await database.Database.ControlledServers.AddAsync(server2);
        await database.Database.SaveChangesAsync();

        var job = new RunMarkedServerMaintenanceJob(logger, database.NotificationsEnabledDatabase, jobClientMock,
            ec2Mock, sshMock);

        await job.Execute(CancellationToken.None);

        Assert.Equal(ServerStatus.Running, server1.Status);
        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(ServerStatus.Terminated, server2.Status);

        await ec2Mock.Received().TerminateInstance(secondServerInstance);
        await ec2Mock.DidNotReceiveWithAnyArgs().LaunchNewInstance();
        await ec2Mock.DidNotReceiveWithAnyArgs().ResumeInstance(default!);

        sshMock.DidNotReceiveWithAnyArgs().ConnectTo(default!, default!);
        sshMock.DidNotReceiveWithAnyArgs().RunCommand(default!);
    }

    [Fact]
    public async Task MaintenanceJobTests_OnlyFirstOfEachType()
    {
        const string keyName = "server-ssh-key";
        const string keyName2 = "server-ssh-key2";

        const string firstServerInstance = "5678";
        const string secondServerInstance = "9007";

        const string firstExternalAddress = "127.0.0.1";
        const string secondExternalAddress = "168.0.0.1";

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var database = new EditableInMemoryDatabaseFixtureWithNotifications(notificationsMock,
            "MarkedServerMaintenanceOnlyFirstOfType");

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.TerminateInstance(firstServerInstance).Returns(Task.CompletedTask);

        var sshMock = Substitute.For<IExternalServerSSHAccess>();
        sshMock.ConnectTo(firstExternalAddress, keyName);
        sshMock.RunCommand(Arg.Any<string>()).Returns(new BaseSSHAccess.CommandResult
            { ExitCode = 0, Result = "Output would go here" });

        var server1 = new ControlledServer
        {
            Id = 1,
            InstanceId = firstServerInstance,
            Status = ServerStatus.Running,
            WantsMaintenance = true,
        };

        var server2 = new ControlledServer
        {
            Id = 2,
            InstanceId = secondServerInstance,
            Status = ServerStatus.Stopped,
            WantsMaintenance = true,
        };

        var server3 = new ExternalServer
        {
            Id = 3,
            PublicAddress = IPAddress.Parse(firstExternalAddress),
            Status = ServerStatus.Running,
            WantsMaintenance = true,
            SSHKeyFileName = keyName,
        };

        var server4 = new ExternalServer
        {
            Id = 4,
            PublicAddress = IPAddress.Parse(secondExternalAddress),
            Status = ServerStatus.Running,
            WantsMaintenance = true,
            SSHKeyFileName = keyName2,
        };

        await database.Database.ControlledServers.AddAsync(server1);
        await database.Database.ControlledServers.AddAsync(server2);
        await database.Database.ExternalServers.AddAsync(server3);
        await database.Database.ExternalServers.AddAsync(server4);
        await database.Database.SaveChangesAsync();

        // Only first server of each type should get processed initially
        var job = new RunMarkedServerMaintenanceJob(logger, database.NotificationsEnabledDatabase, jobClientMock,
            ec2Mock, sshMock);

        await job.Execute(CancellationToken.None);

        Assert.Equal(ServerStatus.Terminated, server1.Status);
        Assert.Equal(ServerStatus.Stopped, server2.Status);
        Assert.Equal(ServerStatus.Stopping, server3.Status);
        Assert.Equal(ServerStatus.Running, server4.Status);

        await ec2Mock.Received().TerminateInstance(firstServerInstance);
        await ec2Mock.DidNotReceive().TerminateInstance(secondServerInstance);
        sshMock.Received().ConnectTo(firstExternalAddress, keyName);
        sshMock.Received().RunCommand(Arg.Any<string>());
        sshMock.Received().Reboot();

        sshMock.DidNotReceive().ConnectTo(secondExternalAddress, keyName2);

        // When running the job the second time it processes the second set of servers
        ec2Mock.TerminateInstance(secondServerInstance).Returns(Task.CompletedTask);

        await job.Execute(CancellationToken.None);

        Assert.Equal(ServerStatus.Terminated, server1.Status);
        Assert.Equal(ServerStatus.Terminated, server2.Status);
        Assert.Equal(ServerStatus.Stopping, server3.Status);
        Assert.Equal(ServerStatus.Stopping, server4.Status);

        await ec2Mock.Received().TerminateInstance(secondServerInstance);

        sshMock.Received().ConnectTo(secondExternalAddress, keyName2);
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
