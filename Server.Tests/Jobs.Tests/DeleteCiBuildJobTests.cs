namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Jobs;
using Server.Models;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class DeleteCiBuildJobTests : IDisposable
{
    private readonly XunitLogger<DeleteCiBuildJob> logger;

    public DeleteCiBuildJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<DeleteCiBuildJob>(output);
    }

    [Fact]
    public async Task DeleteCiBuildJob_DeletesBuildResourcesCorrectly()
    {
        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(DeleteCiBuildJob_DeletesBuildResourcesCorrectly)).Options);

        var ciProject = new CiProject();
        await database.CiProjects.AddAsync(ciProject);
        await database.SaveChangesAsync();

        var ciBuild1 = new CiBuild
        {
            CiBuildId = 1,
            CiProject = ciProject,
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

        var section1 = new CiJobOutputSection
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild1.CiBuildId,
            CiJobId = job1.CiJobId,
            CiJobOutputSectionId = 1,
            Output = "Some text",
        };
        await database.CiJobOutputSections.AddAsync(section1);
        job1.CiJobOutputSections.Add(section1);

        var ciBuild2 = new CiBuild
        {
            CiBuildId = 2,
            CiProject = ciProject,
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

        var section2 = new CiJobOutputSection
        {
            CiProjectId = ciProject.Id,
            CiBuildId = ciBuild2.CiBuildId,
            CiJobId = job2.CiJobId,
            CiJobOutputSectionId = 1,
            Output = "Some text",
        };
        await database.CiJobOutputSections.AddAsync(section2);
        job2.CiJobOutputSections.Add(section2);

        await database.SaveChangesAsync();

        var job = new DeleteCiBuildJob(logger, database);

        await job.Execute(ciBuild2.CiProjectId, ciBuild2.CiBuildId, CancellationToken.None);

        Assert.NotNull(await database.CiBuilds.FindAsync(ciBuild1.CiProjectId, ciBuild1.CiBuildId));
        Assert.NotNull(await database.CiJobs.FindAsync(job1.CiProjectId, job1.CiBuildId, job1.CiJobId));
        Assert.NotNull(await database.CiJobOutputSections.FindAsync(section1.CiProjectId, section1.CiBuildId,
            section1.CiJobId, section1.CiJobOutputSectionId));

        Assert.Null(await database.CiBuilds.FindAsync(ciBuild2.CiProjectId, ciBuild2.CiBuildId));
        Assert.Null(await database.CiJobs.FindAsync(job2.CiProjectId, job2.CiBuildId, job2.CiJobId));
        Assert.Null(await database.CiJobOutputSections.FindAsync(section2.CiProjectId, section2.CiBuildId,
            section2.CiJobId, section2.CiJobOutputSectionId));
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
