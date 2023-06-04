namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class DeleteOldCIJobOutputJobTests : IDisposable
{
    private readonly XunitLogger<DeleteOldCIJobOutputJob> logger;

    public DeleteOldCIJobOutputJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<DeleteOldCIJobOutputJob>(output);
    }

    [Fact]
    public async Task DeleteOldCIJobOutputJob_PurgesRightSections()
    {
        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        await using var database = new NotificationsEnabledDb(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(nameof(DeleteOldCIJobOutputJob_PurgesRightSections))
                .Options, notificationsMock.Object);

        var ciProject = new CiProject();

        await database.CiProjects.AddAsync(ciProject);

        var ciBuild = new CiBuild
        {
            CiBuildId = 1,
            CiProject = ciProject,
        };
        await database.CiBuilds.AddAsync(ciBuild);

        var job1 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild.CiBuildId,
            CiJobId = 1,
            CreatedAt = DateTime.UtcNow,
        };
        await database.CiJobs.AddAsync(job1);

        var job2 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild.CiBuildId,
            CiJobId = 2,
            CreatedAt = DateTime.UtcNow - AppInfo.DeleteFailedJobLogsAfter,
            Succeeded = false,
        };
        await database.CiJobs.AddAsync(job2);

        var job3 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild.CiBuildId,
            CiJobId = 3,
            CreatedAt = DateTime.UtcNow - AppInfo.DeleteSuccessfulJobLogsAfter,
            Succeeded = true,
        };
        await database.CiJobs.AddAsync(job3);

        var job4 = new CiJob
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild.CiBuildId,
            CiJobId = 4,
            CreatedAt = DateTime.UtcNow - AppInfo.DeleteSuccessfulJobLogsAfter + TimeSpan.FromSeconds(30),
            Succeeded = true,
        };
        await database.CiJobs.AddAsync(job4);

        await database.SaveChangesAsync();

        var job1Section1 = await CreateJobSection(database, job1, 1, "Test section");

        var job2Section1 = await CreateJobSection(database, job2, 1, "Test section");
        var job2Section2 = await CreateJobSection(database, job2, 2, "Another section");

        var job3Section1 = await CreateJobSection(database, job3, 1, "Test section");

        var job4Section1 = await CreateJobSection(database, job4, 1, "Test section");

        var job = new DeleteOldCIJobOutputJob(logger, database);

        await job.Execute(CancellationToken.None);

        Assert.NotNull(await FindSectionAgain(database, job1Section1));
        Assert.False(job1.OutputPurged);

        Assert.Null(await FindSectionAgain(database, job2Section1));
        Assert.Null(await FindSectionAgain(database, job2Section2));
        Assert.True(job2.OutputPurged);

        Assert.Null(await FindSectionAgain(database, job3Section1));
        Assert.True(job3.OutputPurged);

        Assert.NotNull(await FindSectionAgain(database, job4Section1));
        Assert.False(job4.OutputPurged);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static ValueTask<CiJobOutputSection?> FindSectionAgain(ApplicationDbContext database,
        CiJobOutputSection section)
    {
        return database.CiJobOutputSections.FindAsync(section.CiProjectId, section.CiBuildId, section.CiJobId,
            section.CiJobOutputSectionId);
    }

    private static async Task<CiJobOutputSection> CreateJobSection(ApplicationDbContext database, CiJob job,
        long sectionId, string text)
    {
        var section = new CiJobOutputSection
        {
            CiProjectId = job.CiProjectId,
            CiBuildId = job.CiBuildId,
            CiJobId = job.CiJobId,
            CiJobOutputSectionId = sectionId,
            Output = text,
            OutputLength = text.Length,
        };

        job.CiJobOutputSections.Add(section);
        await database.CiJobOutputSections.AddAsync(section);
        return section;
    }
}
