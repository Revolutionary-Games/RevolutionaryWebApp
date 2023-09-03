namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class DeleteOldCIBuildsJobTests : IDisposable
{
    private static readonly TimeSpan NotExpiredTime = AppInfo.DeleteCIBuildsAfter / 2;

    private static readonly TimeSpan
        ExpiredTimespan = AppInfo.DeleteCIBuildsAfter + TimeSpan.FromSeconds(10);

    private readonly XunitLogger<DeleteOldCIBuildsJob> logger;

    public DeleteOldCIBuildsJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<DeleteOldCIBuildsJob>(output);
    }

    [Fact]
    public async Task DeleteOldCIBuildsJob_DeletesAnOldBuild()
    {
        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(DeleteOldCIBuildsJob_DeletesAnOldBuild)).Options);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var ciProject = new CiProject();
        await database.CiProjects.AddAsync(ciProject);
        await database.SaveChangesAsync();

        var ciBuild1 = new CiBuild
        {
            CiBuildId = 1,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - ExpiredTimespan,
        };
        await database.CiBuilds.AddAsync(ciBuild1);

        var job1 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild1.CiBuildId,
            CiJobId = 1,
            OutputPurged = true,
        };
        await database.CiJobs.AddAsync(job1);

        var ciBuild2 = new CiBuild
        {
            CiBuildId = 2,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - ExpiredTimespan,
        };
        await database.CiBuilds.AddAsync(ciBuild2);

        var job2 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild2.CiBuildId,
            CiJobId = 1,
            OutputPurged = true,
        };
        await database.CiJobs.AddAsync(job2);

        await database.SaveChangesAsync();

        var job = new DeleteOldCIBuildsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        jobClientMock.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task DeleteOldCIBuildsJob_OneOldBuildIsDeletedWithNewPresent()
    {
        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(DeleteOldCIBuildsJob_OneOldBuildIsDeletedWithNewPresent)).Options);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var ciProject = new CiProject();
        await database.CiProjects.AddAsync(ciProject);
        await database.SaveChangesAsync();

        var ciBuild1 = new CiBuild
        {
            CiBuildId = 1,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - ExpiredTimespan,
        };
        await database.CiBuilds.AddAsync(ciBuild1);

        var job1 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild1.CiBuildId,
            CiJobId = 1,
            OutputPurged = true,
        };
        await database.CiJobs.AddAsync(job1);

        var ciBuild2 = new CiBuild
        {
            CiBuildId = 2,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - NotExpiredTime,
        };
        await database.CiBuilds.AddAsync(ciBuild2);

        var job2 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild2.CiBuildId,
            CiJobId = 1,
            OutputPurged = true,
        };
        await database.CiJobs.AddAsync(job2);

        var ciBuild3 = new CiBuild
        {
            CiBuildId = 3,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - NotExpiredTime,
        };
        await database.CiBuilds.AddAsync(ciBuild3);

        var job3 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild3.CiBuildId,
            CiJobId = 1,
            OutputPurged = true,
        };
        await database.CiJobs.AddAsync(job3);

        await database.SaveChangesAsync();

        var job = new DeleteOldCIBuildsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        jobClientMock.Received(1).Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task DeleteOldCIBuildsJob_SingleBuildIsNotDeleted()
    {
        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(DeleteOldCIBuildsJob_SingleBuildIsNotDeleted)).Options);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var ciProject = new CiProject();
        await database.CiProjects.AddAsync(ciProject);
        await database.SaveChangesAsync();

        var ciBuild1 = new CiBuild
        {
            CiBuildId = 1,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - ExpiredTimespan,
        };
        await database.CiBuilds.AddAsync(ciBuild1);

        var job1 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild1.CiBuildId,
            CiJobId = 1,
            OutputPurged = true,
        };
        await database.CiJobs.AddAsync(job1);

        await database.SaveChangesAsync();

        var job = new DeleteOldCIBuildsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task DeleteOldCIBuildsJob_NewBuildIsNotDeleted()
    {
        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(DeleteOldCIBuildsJob_NewBuildIsNotDeleted)).Options);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var ciProject = new CiProject();
        await database.CiProjects.AddAsync(ciProject);
        await database.SaveChangesAsync();

        var ciBuild1 = new CiBuild
        {
            CiBuildId = 1,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - NotExpiredTime,
        };
        await database.CiBuilds.AddAsync(ciBuild1);

        var job1 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild1.CiBuildId,
            CiJobId = 1,
            OutputPurged = true,
        };
        await database.CiJobs.AddAsync(job1);

        var ciBuild2 = new CiBuild
        {
            CiBuildId = 2,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - NotExpiredTime,
        };
        await database.CiBuilds.AddAsync(ciBuild2);

        var job2 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild2.CiBuildId,
            CiJobId = 1,
            OutputPurged = true,
        };
        await database.CiJobs.AddAsync(job2);

        await database.SaveChangesAsync();

        var job = new DeleteOldCIBuildsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task DeleteOldCIBuildsJob_BuildsWithNonPurgedOutputAreSkipped()
    {
        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(DeleteOldCIBuildsJob_BuildsWithNonPurgedOutputAreSkipped)).Options);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var ciProject = new CiProject();
        await database.CiProjects.AddAsync(ciProject);
        await database.SaveChangesAsync();

        var ciBuild1 = new CiBuild
        {
            CiBuildId = 1,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - ExpiredTimespan,
        };
        await database.CiBuilds.AddAsync(ciBuild1);

        var job1 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild1.CiBuildId,
            CiJobId = 1,
            OutputPurged = false,
        };
        await database.CiJobs.AddAsync(job1);

        var ciBuild2 = new CiBuild
        {
            CiBuildId = 2,
            CiProject = ciProject,
            CreatedAt = DateTime.UtcNow - ExpiredTimespan,
        };
        await database.CiBuilds.AddAsync(ciBuild2);

        var job2 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild2.CiBuildId,
            CiJobId = 1,
            OutputPurged = false,
        };
        await database.CiJobs.AddAsync(job2);

        await database.SaveChangesAsync();

        var job = new DeleteOldCIBuildsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
