namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using Xunit;

public class TranslationProjectsControllerTests
{
    [Fact]
    public async Task QualityChecksAreConfigurableAndChangesQueueAFullRecheck()
    {
        await using var database = CreateDatabase();
        var actor = new User("admin@example.com", "Admin");
        database.Users.Add(actor);
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var jobs = Substitute.For<IBackgroundJobClient>();
        var controller = new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance,
            database, jobs)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        var request = new TranslationProjectConfigurationDTO
        {
            Slug = "test", Name = "Test", Format = TranslationFormat.Manual,
            QualityChecks = TranslationQualityCheck.CSharpFormatting,
        };
        var created = Assert.IsType<CreatedAtActionResult>((await controller.Create(request)).Result);
        var configuration = Assert.IsType<TranslationProjectConfigurationDTO>(created.Value);
        Assert.Equal(request.QualityChecks, configuration.QualityChecks);

        request.QualityChecks |= TranslationQualityCheck.SentenceConsistency;
        Assert.Equal(request.QualityChecks, (await controller.Update(configuration.Id, request)).Value!.QualityChecks);
        var project = await database.TranslationProjects.SingleAsync();
        Assert.Equal(request.QualityChecks, project.GetDTO(false).QualityChecks);
        jobs.Received(1).Create(Arg.Is<Job>(job => job.Type == typeof(CheckTranslationQualityJob) &&
            (long)job.Args[0] == project.Id && (bool)job.Args[1]), Arg.Any<IState>());
    }

    [Fact]
    public async Task RejectsUnknownQualityChecks()
    {
        await using var database = CreateDatabase();
        var controller = new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance,
            database, Substitute.For<IBackgroundJobClient>());
        Assert.IsType<BadRequestObjectResult>((await controller.Create(new TranslationProjectConfigurationDTO
        {
            Slug = "test", Name = "Test", Format = TranslationFormat.Manual,
            QualityChecks = (TranslationQualityCheck)4,
        })).Result);
        Assert.Empty(database.TranslationProjects);
    }

    [Fact]
    public async Task SourceLinkConfigurationIsSavedAndExposedToTranslatorsAndCanBeCleared()
    {
        await using var database = CreateDatabase();
        var actor = new User("admin@example.com", "Admin");
        database.Users.Add(actor);
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance,
            database, Substitute.For<IBackgroundJobClient>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        var request = new TranslationProjectConfigurationDTO
        {
            Slug = "test", Name = "Test", Format = TranslationFormat.Manual,
            SourceLocationBaseUrl = "https://example.com/repository/locales/",
            SourceLocationUrlTemplate = "{path}?line={line}",
        };

        var created = Assert.IsType<CreatedAtActionResult>((await controller.Create(request)).Result);
        var configuration = Assert.IsType<TranslationProjectConfigurationDTO>(created.Value);
        Assert.Equal(request.SourceLocationBaseUrl, configuration.SourceLocationBaseUrl);
        Assert.Equal(request.SourceLocationUrlTemplate, configuration.SourceLocationUrlTemplate);
        database.ChangeTracker.Clear();
        var project = await database.TranslationProjects.SingleAsync();
        Assert.Equal(request.SourceLocationBaseUrl, project.GetDTO(false).SourceLocationBaseUrl);
        Assert.Equal(request.SourceLocationUrlTemplate, project.GetDTO(false).SourceLocationUrlTemplate);

        request.SourceLocationBaseUrl = string.Empty;
        request.SourceLocationUrlTemplate = string.Empty;
        var updated = (await controller.Update(project.Id, request)).Value!;
        Assert.Null(updated.SourceLocationBaseUrl);
        Assert.Null(updated.SourceLocationUrlTemplate);
    }

    [Theory]
    [InlineData("javascript:alert(1)/", null)]
    [InlineData("https://example.com/no-slash", null)]
    [InlineData("https://example.com/", "{path}#L{unknown}")]
    public async Task RejectsInvalidSourceLinkConfiguration(string baseUrl, string? template)
    {
        await using var database = CreateDatabase();
        var controller = new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance,
            database, Substitute.For<IBackgroundJobClient>());
        var request = new TranslationProjectConfigurationDTO
        {
            Slug = "test", Name = "Test", Format = TranslationFormat.Manual,
            SourceLocationBaseUrl = baseUrl, SourceLocationUrlTemplate = template,
        };

        Assert.IsType<BadRequestObjectResult>((await controller.Create(request)).Result);
        Assert.Empty(await database.TranslationProjects.ToListAsync());
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }
}
