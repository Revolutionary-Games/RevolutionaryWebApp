namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Jobs;
using Server.Models;
using Server.Services;
using Shared.Models;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class TerminateLongStoppedServersJobTests : IDisposable
{
    private const string Server1InstanceId = "id-1231245";
    private const string Server2InstanceId = "id-6789012";
    private const string Server3InstanceId = "id-3333012";

    private readonly XunitLogger<TerminateLongStoppedServersJob> logger;

    public TerminateLongStoppedServersJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<TerminateLongStoppedServersJob>(output);
    }

    [Fact]
    public async Task TerminateStoppedServers_TerminatesLongStoppedServers()
    {
        var ec2Mock = Substitute.For<IEC2Controller>();
        ec2Mock.TerminateInstance(Server1InstanceId).Returns(Task.CompletedTask);
        ec2Mock.Configured.Returns(true);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new KeyValuePair<string, string?>[]
        {
            new("CI:TerminateStoppedServersDelayHours", "24"),
        }).Build();

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("TerminateLongStoppedServersTerminates")
            .Options, notificationsMock);

        var server1 = new ControlledServer
        {
            Status = ServerStatus.Stopped,
            InstanceId = Server1InstanceId,
            UpdatedAt = DateTime.UtcNow - TimeSpan.FromDays(10),
        };

        await database.ControlledServers.AddAsync(server1);

        var server2 = new ControlledServer
        {
            Status = ServerStatus.Stopped,
            InstanceId = Server2InstanceId,
            UpdatedAt = DateTime.UtcNow,
        };

        await database.ControlledServers.AddAsync(server2);

        var server3 = new ControlledServer
        {
            Status = ServerStatus.Running,
            InstanceId = Server3InstanceId,
            UpdatedAt = DateTime.UtcNow - TimeSpan.FromDays(10),
        };

        await database.ControlledServers.AddAsync(server3);
        await database.SaveChangesAsync();

        Assert.Equal(ServerStatus.Stopped, server1.Status);
        Assert.Equal(ServerStatus.Stopped, server2.Status);
        Assert.Equal(ServerStatus.Running, server3.Status);

        await new TerminateLongStoppedServersJob(logger, config, database, ec2Mock).Execute(CancellationToken
            .None);

        Assert.Equal(ServerStatus.Terminated, server1.Status);
        Assert.Equal(ServerStatus.Stopped, server2.Status);
        Assert.Equal(ServerStatus.Running, server3.Status);

        await new TerminateLongStoppedServersJob(logger, config, database, ec2Mock).Execute(CancellationToken
            .None);

        Assert.Equal(ServerStatus.Terminated, server1.Status);
        Assert.Equal(ServerStatus.Stopped, server2.Status);
        Assert.Equal(ServerStatus.Running, server3.Status);

#pragma warning disable CS4014
        ec2Mock.Received().TerminateInstance(Server1InstanceId);
#pragma warning restore CS4014
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
