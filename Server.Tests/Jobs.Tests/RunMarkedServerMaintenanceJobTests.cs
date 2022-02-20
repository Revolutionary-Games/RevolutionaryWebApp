namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Hangfire;
using Moq;
using Server.Jobs;
using Server.Models;
using Server.Services;
using Shared.Models;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public class RunMarkedServerMaintenanceJobTests
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

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();
        var database = new EditableInMemoryDatabaseFixtureWithNotifications(notificationsMock.Object,
            "MarkedServerMaintenanceIgnoreReserved");

        var jobClientMock = new Mock<IBackgroundJobClient>();
        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.TerminateInstance(secondServerInstance)).Returns(Task.CompletedTask).Verifiable();

        var sshMock = new Mock<IExternalServerSSHAccess>();

        var server1 = new ControlledServer()
        {
            Id = 1,
            InstanceId = "1234",
            Status = ServerStatus.Running,
            ReservationType = ServerReservationType.CIJob,
            ReservedFor = 1,
            WantsMaintenance = true,
        };

        var server2 = new ControlledServer()
        {
            Id = 2,
            InstanceId = secondServerInstance,
            Status = ServerStatus.Stopped,
            WantsMaintenance = true,
        };

        await database.Database.ControlledServers.AddAsync(server1);
        await database.Database.ControlledServers.AddAsync(server2);
        await database.Database.SaveChangesAsync();

        var job = new RunMarkedServerMaintenanceJob(logger, database.NotificationsEnabledDatabase, jobClientMock.Object,
            ec2Mock.Object, sshMock.Object);

        await job.Execute(CancellationToken.None);

        Assert.Equal(ServerStatus.Running, server1.Status);
        Assert.Equal(ServerReservationType.CIJob, server1.ReservationType);
        Assert.Equal(ServerStatus.Terminated, server2.Status);

        ec2Mock.Verify();
        ec2Mock.VerifyNoOtherCalls();
        sshMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MaintenanceJobTests_OnlyFirstOfEachType()
    {
        const string keyName = "server-ssh-key";
        const string keyName2 = "server-ssh-key2";

        const string firstServerInstance = "5678";
        const string secondServerInstance = "5678";

        const string firstExternalAddress = "127.0.0.1";
        const string secondExternalAddress = "168.0.0.1";

        var notificationsMock = new Mock<IModelUpdateNotificationSender>();
        var database = new EditableInMemoryDatabaseFixtureWithNotifications(notificationsMock.Object,
            "MarkedServerMaintenanceOnlyFirstOfType");

        var jobClientMock = new Mock<IBackgroundJobClient>();

        var ec2Mock = new Mock<IEC2Controller>();
        ec2Mock.Setup(ec2 => ec2.TerminateInstance(firstServerInstance)).Returns(Task.CompletedTask).Verifiable();

        var sshMock = new Mock<IExternalServerSSHAccess>();
        sshMock.Setup(ssh => ssh.ConnectTo(firstExternalAddress, keyName)).Verifiable();
        sshMock.Setup(ssh => ssh.RunCommand(It.IsAny<string>())).Returns(new BaseSSHAccess.CommandResult()
            { ExitCode = 0, Result = "Output would go here" }).Verifiable();
        sshMock.Setup(ssh => ssh.Reboot()).Verifiable();

        var server1 = new ControlledServer()
        {
            Id = 1,
            InstanceId = firstServerInstance,
            Status = ServerStatus.Running,
            WantsMaintenance = true,
        };

        var server2 = new ControlledServer()
        {
            Id = 2,
            InstanceId = secondServerInstance,
            Status = ServerStatus.Stopped,
            WantsMaintenance = true,
        };

        var server3 = new ExternalServer()
        {
            Id = 3,
            PublicAddress = IPAddress.Parse(firstExternalAddress),
            Status = ServerStatus.Running,
            WantsMaintenance = true,
            SSHKeyFileName = keyName,
        };

        var server4 = new ExternalServer()
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
        var job = new RunMarkedServerMaintenanceJob(logger, database.NotificationsEnabledDatabase, jobClientMock.Object,
            ec2Mock.Object, sshMock.Object);

        await job.Execute(CancellationToken.None);

        Assert.Equal(ServerStatus.Terminated, server1.Status);
        Assert.Equal(ServerStatus.Stopped, server2.Status);
        Assert.Equal(ServerStatus.Stopping, server3.Status);
        Assert.Equal(ServerStatus.Running, server4.Status);

        ec2Mock.Verify();
        ec2Mock.VerifyNoOtherCalls();
        sshMock.Verify();
        sshMock.VerifyNoOtherCalls();

        // When running the job the second time it processes the second set of servers
        ec2Mock.Setup(ec2 => ec2.TerminateInstance(secondServerInstance)).Returns(Task.CompletedTask).Verifiable();

        sshMock.Setup(ssh => ssh.ConnectTo(secondExternalAddress, keyName2)).Verifiable();

        await job.Execute(CancellationToken.None);

        Assert.Equal(ServerStatus.Terminated, server1.Status);
        Assert.Equal(ServerStatus.Terminated, server2.Status);
        Assert.Equal(ServerStatus.Stopping, server3.Status);
        Assert.Equal(ServerStatus.Stopping, server4.Status);

        ec2Mock.Verify();
        ec2Mock.VerifyNoOtherCalls();
        sshMock.Verify();
        sshMock.VerifyNoOtherCalls();
    }
}
