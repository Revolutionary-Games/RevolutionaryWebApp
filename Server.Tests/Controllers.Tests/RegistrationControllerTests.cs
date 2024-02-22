namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Threading.Tasks;
using Dummies;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Authorization;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared.Forms;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class RegistrationControllerTests : IDisposable
{
    private const string RegistrationCode = "Code";
    private readonly XunitLogger<RegistrationController> logger;

    private readonly DbContextOptions<ApplicationDbContext> dbOptions =
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("RegistrationTestDB").Options;

    private readonly DummyRegistrationStatus dummyRegistrationStatus = new()
    {
        RegistrationEnabled = true,
        RegistrationCode = RegistrationCode,
    };

    public RegistrationControllerTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<RegistrationController>(output);
    }

    [Fact]
    public async Task Get_ReturnsRegistrationEnabledStatus()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database = new NotificationsEnabledDb(dbOptions, notificationsMock);

        var controller = new RegistrationController(logger, new DummyRegistrationStatus
        {
            RegistrationEnabled = true,
            RegistrationCode = "abc123",
        }, Substitute.For<ITokenVerifier>(), database, jobClientMock);

        var result = controller.Get();

        Assert.True(result);

        notificationsMock.DidNotReceiveWithAnyArgs().OnChangesDetected(default, default!, default);
    }

    [Fact]
    public async Task Get_ReturnsRegistrationDisabledStatus()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database = new NotificationsEnabledDb(dbOptions, notificationsMock);

        var controller = new RegistrationController(logger, new DummyRegistrationStatus
        {
            RegistrationEnabled = false,
        }, Substitute.For<ITokenVerifier>(), database, jobClientMock);

        var result = controller.Get();

        Assert.False(result);

        notificationsMock.DidNotReceiveWithAnyArgs().OnChangesDetected(default, default!, default);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Registration_FailsOnInvalidCSRF(string? csrfValue)
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(csrfValue!, null, false).Returns(false);

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database = new NotificationsEnabledDb(dbOptions, notificationsMock);
        Assert.Empty(database.Users);

        var controller = new RegistrationController(logger, dummyRegistrationStatus, csrfMock, database,
            jobClientMock);

        var result = await controller.Post(new RegistrationFormData
        {
            CSRF = csrfValue!, Email = "test@example.com", Name = "test", Password = "password12345",
            RegistrationCode = RegistrationCode,
        });

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);

        Assert.Equal(400, objectResult.StatusCode);
        Assert.Empty(database.Users);

        csrfMock.Received().IsValidCSRFToken(csrfValue!, null, false);
    }

    [Fact]
    public async Task Registration_FailsOnInvalidCode()
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(ArgExtension.IsNotNull<string>(), null, false).Returns(true);

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database = new NotificationsEnabledDb(dbOptions, notificationsMock);

        var controller = new RegistrationController(logger, dummyRegistrationStatus,
            csrfMock, database, jobClientMock);

        var result = await controller.Post(new RegistrationFormData
        {
            CSRF = "aValue", Email = "test@example.com", Name = "test", Password = "password12345",
            RegistrationCode = RegistrationCode + "a",
        });

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);

        Assert.Equal(400, objectResult.StatusCode);
        Assert.Empty(database.Users);

        csrfMock.Received().IsValidCSRFToken(ArgExtension.IsNotNull<string>(), null, false);
    }

    [Fact]
    public async Task Registration_SucceedsAndCreatesUser()
    {
        var csrfValue = "JustSomeRandomString";

        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(csrfValue, null, false).Returns(true);
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database = new NotificationsEnabledDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("RegistrationTestDBWritable")
                .Options, notificationsMock);

        var controller = new RegistrationController(logger, dummyRegistrationStatus, csrfMock, database,
            jobClientMock);

        var result = await controller.Post(new RegistrationFormData
        {
            CSRF = csrfValue, Email = "test@example.com", Name = "test", Password = "password12345",
            RegistrationCode = RegistrationCode,
        });

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);

        Assert.Equal(201, objectResult.StatusCode);

        Assert.NotEmpty(database.Users);
        var user = await database.Users.FirstAsync();

        Assert.Equal("test@example.com", user.Email);
        Assert.Equal("test", user.UserName);
        Assert.NotEqual("password12345", user.PasswordHash);
        Assert.NotNull(user.PasswordHash);
        Assert.True(Passwords.CheckPassword(user.PasswordHash, "password12345"));

        notificationsMock.OnChangesDetected(EntityState.Added, Arg.Any<User>(), false);
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
