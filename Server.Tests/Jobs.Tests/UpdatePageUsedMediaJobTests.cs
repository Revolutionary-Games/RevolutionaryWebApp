namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Server.Jobs.Pages;
using Server.Models;
using Server.Models.Pages;
using Xunit;

public class UpdatePageUsedMediaJobTests
{
    [Fact]
    public async Task ExecuteWarnsAboutMissingMediaAndSavesValidUsage()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        await using var database = new ApplicationDbContext(options);
        var validId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var page = new VersionedPage("Page with two images")
        {
            LatestContent = $"![Valid](media:png:{validId}) ![Missing](media:png:{missingId})",
        };
        database.VersionedPages.Add(page);
        database.MediaFiles.Add(new MediaFile("image.png", validId, 1, null));
        await database.SaveChangesAsync();

        var logger = Substitute.For<ILogger<UpdatePageUsedMediaJob>>();
        var job = new UpdatePageUsedMediaJob(logger, database);
        await job.Execute(page.Id, CancellationToken.None);

        var usage = await database.MediaFileUsages.SingleAsync();
        Assert.Equal(validId, usage.MediaFileGuid);
        Assert.Equal(page.Id, usage.UsedByResource);
        Assert.Equal(MediaFileUsage.UsageType.Page, usage.Usage);

        var warning = Assert.Single(logger.ReceivedCalls(), call =>
            call.GetMethodInfo().Name == nameof(ILogger.Log) && Equals(call.GetArguments()[0], LogLevel.Warning));
        Assert.Equal($"Skipping missing media file ({missingId}) referenced by page ({page.Id})",
            warning.GetArguments()[2]?.ToString());
    }
}
