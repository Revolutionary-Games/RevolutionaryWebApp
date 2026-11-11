namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using Xunit;

public class TranslationProjectsControllerTests
{
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
