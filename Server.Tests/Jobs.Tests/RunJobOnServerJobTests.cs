namespace ThriveDevCenter.Server.Tests.Jobs.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Fixtures;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Moq;
    using Server.Jobs;
    using Server.Models;
    using Server.Services;
    using Shared.Models;
    using Utilities;
    using Xunit;
    using Xunit.Abstractions;

    public class RunJobOnServerJobTests
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

            var notificationsMock = new Mock<IModelUpdateNotificationSender>();
            await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("RunJobRunsOnExternalServer")
                .Options, notificationsMock.Object);

            var controlledSSHMock = new Mock<IControlledServerSSHAccess>();

            var externalSSHMock = new Mock<IExternalServerSSHAccess>();
            externalSSHMock.Setup(ssh => ssh.RunCommand(BaseCIJobManagingJob.DiskUsageCheckCommand)).Returns(
                    new BaseSSHAccess.CommandResult()
                    {
                        Result = DiskUsageResult,
                        ExitCode = 0,
                    })
                .Verifiable();
            externalSSHMock.Setup(ssh => ssh.RunCommand(It.Is<string>(s => s.Contains("~/CIExecutor")))).Returns(
                new BaseSSHAccess.CommandResult()
                {
                    ExitCode = 0,
                }).Verifiable();
            externalSSHMock.Setup(ssh => ssh.ConnectTo(address.ToString(), keyName)).Verifiable();

            var remoteDownloadsMock = new Mock<IGeneralRemoteDownloadUrls>();
            remoteDownloadsMock
                .Setup(download => download.CreateDownloadFor(It.IsAny<StorageFile>(), It.IsAny<TimeSpan>()))
                .Returns("https://dummy.download/url").Verifiable();

            var githubStatusAPI = new Mock<IGithubCommitStatusReporter>();

            var jobClientMock = new Mock<IBackgroundJobClient>();

            var job = new RunJobOnServerJob(logger, new ConfigurationBuilder().AddInMemoryCollection(
                    new KeyValuePair<string, string>[]
                    {
                        new("BaseUrl", "http://localhost:5000/"),
                        new("CI:ServerCleanUpDiskUsePercentage", "80"),
                    }).Build(), database, controlledSSHMock.Object, externalSSHMock.Object, jobClientMock.Object,
                githubStatusAPI.Object, remoteDownloadsMock.Object);

            CIProjectTestDatabaseData.Seed(database);
            var buildJob = new CiJob()
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

            var server = new ExternalServer()
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

            controlledSSHMock.VerifyNoOtherCalls();
            externalSSHMock.Verify();
            remoteDownloadsMock.Verify();
            githubStatusAPI.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task RunJobOnServer_RunsCorrectlyForInternalServer()
        {
            var address = IPAddress.Parse("1.2.3.4");

            var notificationsMock = new Mock<IModelUpdateNotificationSender>();
            await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("RunJobRunsOnInternalServer")
                .Options, notificationsMock.Object);

            var controlledSSHMock = new Mock<IControlledServerSSHAccess>();
            controlledSSHMock.Setup(ssh => ssh.RunCommand(BaseCIJobManagingJob.DiskUsageCheckCommand)).Returns(
                    new BaseSSHAccess.CommandResult()
                    {
                        Result = DiskUsageResult,
                        ExitCode = 0,
                    })
                .Verifiable();
            controlledSSHMock.Setup(ssh => ssh.RunCommand(It.Is<string>(s => s.Contains("~/CIExecutor")))).Returns(
                new BaseSSHAccess.CommandResult()
                {
                    ExitCode = 0,
                }).Verifiable();
            controlledSSHMock.Setup(ssh => ssh.ConnectTo(address.ToString())).Verifiable();

            var externalSSHMock = new Mock<IExternalServerSSHAccess>();

            var remoteDownloadsMock = new Mock<IGeneralRemoteDownloadUrls>();
            remoteDownloadsMock
                .Setup(download => download.CreateDownloadFor(It.IsAny<StorageFile>(), It.IsAny<TimeSpan>()))
                .Returns("https://dummy.download/url").Verifiable();

            var githubStatusAPI = new Mock<IGithubCommitStatusReporter>();

            var jobClientMock = new Mock<IBackgroundJobClient>();

            var job = new RunJobOnServerJob(logger, new ConfigurationBuilder().AddInMemoryCollection(
                    new KeyValuePair<string, string>[]
                    {
                        new("BaseUrl", "http://localhost:5000/"),
                        new("CI:ServerCleanUpDiskUsePercentage", "80"),
                    }).Build(), database, controlledSSHMock.Object, externalSSHMock.Object, jobClientMock.Object,
                githubStatusAPI.Object, remoteDownloadsMock.Object);

            CIProjectTestDatabaseData.Seed(database);
            var buildJob = new CiJob()
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

            var server = new ControlledServer()
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

            controlledSSHMock.Verify();
            externalSSHMock.VerifyNoOtherCalls();
            remoteDownloadsMock.Verify();
            githubStatusAPI.VerifyNoOtherCalls();
        }
    }
}
