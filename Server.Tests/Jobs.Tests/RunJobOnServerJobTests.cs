namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Hangfire;
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

public sealed class RunJobOnServerJobTests : IDisposable
{
    private const string DiskUsageResult =
        @"Tiedostojärjestelmä                1K-lohkot        Käyt    Vapaana Käy% Liitospiste
/dev/nvme0n1p3                    1951850496   583858272 1367783424  30% /
";

    private readonly XunitLogger<RunJobOnServerJob> logger;

    public RunJobOnServerJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<RunJobOnServerJob>(output);
    }

    [Fact]
    public async Task RunJobOnServer_RunsCorrectlyForExternalServer()
    {
        var address = IPAddress.Parse("1.2.3.4");
        var keyName = "ssh_key_name.pem";

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("RunJobRunsOnExternalServer")
            .Options, notificationsMock);

        var controlledSSHMock = Substitute.For<IControlledServerSSHAccess>();

        var externalSSHMock = Substitute.For<IExternalServerSSHAccess>();
        externalSSHMock.RunCommand(BaseCIJobManagingJob.DiskUsageCheckCommand).Returns(
            new BaseSSHAccess.CommandResult
            {
                Result = DiskUsageResult,
                ExitCode = 0,
            });
        externalSSHMock.RunCommand(Arg.Is<string>(s => s.Contains("~/CIExecutor"))).Returns(
            new BaseSSHAccess.CommandResult
            {
                ExitCode = 0,
            });

        var remoteDownloadsMock = Substitute.For<IGeneralRemoteDownloadUrls>();
        remoteDownloadsMock
            .CreateDownloadFor(Arg.Any<StorageFile>(), Arg.Any<TimeSpan>())
            .Returns("https://dummy.download/url");

        var githubStatusAPI = Substitute.For<IGithubCommitStatusReporter>();
        var hashMock = Substitute.For<IRemoteResourceHashCalculator>();

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var job = new RunJobOnServerJob(logger, new ConfigurationBuilder().AddInMemoryCollection(
                new KeyValuePair<string, string?>[]
                {
                    new("BaseUrl", "http://localhost:5000/"),
                    new("CI:ServerCleanUpDiskUsePercentage", "80"),
                }).Build(), database, controlledSSHMock, externalSSHMock, jobClientMock,
            githubStatusAPI, remoteDownloadsMock, hashMock);

        CIProjectTestDatabaseData.Seed(database);
        var buildJob = new CiJob
        {
            CiProjectId = CIProjectTestDatabaseData.CIProjectId,
            CiBuildId = CIProjectTestDatabaseData.CIBuildId,
            CiJobId = 1,
            JobName = "job",
            Image = "test/test:v1",
            CacheSettingsJson = "{}",
            State = CIJobState.WaitingForServer,
        };

        await database.CiJobs.AddAsync(buildJob);

        var server = new ExternalServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            ReservationType = ServerReservationType.CIJob,
            ReservedFor = buildJob.CiJobId,
            PublicAddress = address,
            SSHKeyFileName = keyName,
        };

        await database.ExternalServers.AddAsync(server);
        await database.SaveChangesAsync();

        await job.Execute(buildJob.CiProjectId, buildJob.CiBuildId, buildJob.CiJobId, server.Id, server.IsExternal,
            0, CancellationToken.None);

        Assert.Equal(CIJobState.Running, buildJob.State);

        controlledSSHMock.DidNotReceiveWithAnyArgs().RunCommand(default!);

        externalSSHMock.Received().RunCommand(BaseCIJobManagingJob.DiskUsageCheckCommand);
        externalSSHMock.Received().RunCommand(Arg.Is<string>(s => s.Contains("~/CIExecutor")));
        externalSSHMock.Received().ConnectTo(address.ToString(), keyName);

        remoteDownloadsMock.Received()
            .CreateDownloadFor(Arg.Any<StorageFile>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task RunJobOnServer_RunsCorrectlyForInternalServer()
    {
        var address = IPAddress.Parse("1.2.3.4");

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("RunJobRunsOnInternalServer")
            .Options, notificationsMock);

        var controlledSSHMock = Substitute.For<IControlledServerSSHAccess>();
        controlledSSHMock.RunCommand(BaseCIJobManagingJob.DiskUsageCheckCommand).Returns(
            new BaseSSHAccess.CommandResult
            {
                Result = DiskUsageResult,
                ExitCode = 0,
            });
        controlledSSHMock.RunCommand(Arg.Is<string>(s => s.Contains("~/CIExecutor"))).Returns(
            new BaseSSHAccess.CommandResult
            {
                ExitCode = 0,
            });

        var externalSSHMock = Substitute.For<IExternalServerSSHAccess>();

        var remoteDownloadsMock = Substitute.For<IGeneralRemoteDownloadUrls>();
        remoteDownloadsMock.CreateDownloadFor(Arg.Any<StorageFile>(), Arg.Any<TimeSpan>())
            .Returns("https://dummy.download/url");

        var githubStatusAPI = Substitute.For<IGithubCommitStatusReporter>();
        var hashMock = Substitute.For<IRemoteResourceHashCalculator>();

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var job = new RunJobOnServerJob(logger, new ConfigurationBuilder().AddInMemoryCollection(
                new KeyValuePair<string, string?>[]
                {
                    new("BaseUrl", "http://localhost:5000/"),
                    new("CI:ServerCleanUpDiskUsePercentage", "80"),
                }).Build(), database, controlledSSHMock, externalSSHMock, jobClientMock,
            githubStatusAPI, remoteDownloadsMock, hashMock);

        CIProjectTestDatabaseData.Seed(database);
        var buildJob = new CiJob
        {
            CiProjectId = CIProjectTestDatabaseData.CIProjectId,
            CiBuildId = CIProjectTestDatabaseData.CIBuildId,
            CiJobId = 1,
            JobName = "job",
            Image = "test/test:v1",
            CacheSettingsJson = "{}",
            State = CIJobState.WaitingForServer,
        };

        await database.CiJobs.AddAsync(buildJob);

        var server = new ControlledServer
        {
            Status = ServerStatus.Running,
            ProvisionedFully = true,
            ReservationType = ServerReservationType.CIJob,
            ReservedFor = buildJob.CiJobId,
            PublicAddress = address,
        };

        await database.ControlledServers.AddAsync(server);
        await database.SaveChangesAsync();

        await job.Execute(buildJob.CiProjectId, buildJob.CiBuildId, buildJob.CiJobId, server.Id, server.IsExternal,
            0, CancellationToken.None);

        Assert.Equal(CIJobState.Running, buildJob.State);

        controlledSSHMock.Received().RunCommand(BaseCIJobManagingJob.DiskUsageCheckCommand);
        controlledSSHMock.Received().RunCommand(Arg.Is<string>(s => s.Contains("~/CIExecutor")));
        controlledSSHMock.Received().ConnectTo(address.ToString());

        externalSSHMock.DidNotReceiveWithAnyArgs().RunCommand(default!);

        remoteDownloadsMock.Received().CreateDownloadFor(Arg.Any<StorageFile>(), Arg.Any<TimeSpan>());
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
