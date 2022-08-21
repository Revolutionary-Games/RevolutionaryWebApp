namespace ThriveDevCenter.Server.Tests.Controllers.Tests;

using System.Threading.Tasks;
using Fixtures;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Moq;
using Server.Controllers;
using Server.Services;
using Shared;
using SharedBase.Utilities;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public class CLAControllerTests : IClassFixture<SampleCLADatabase>
{
    private readonly XunitLogger<CLAController> logger;
    private readonly SampleCLADatabase fixture;

    public CLAControllerTests(SampleCLADatabase fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<CLAController>(output);
    }

    [Fact]
    public async Task CLASearch_ReturnsExactEmailResults()
    {
        var mailMock = new Mock<IMailQueue>();
        var storageMock = new Mock<ICLASignatureStorage>();
        var jobsMock = new Mock<IBackgroundJobClient>();

        var controller = new CLAController(logger, new ConfigurationBuilder().Build(),
            fixture.NotificationsEnabledDatabase, storageMock.Object, mailMock.Object, jobsMock.Object);

        var result = await controller.SearchSignatures(fixture.CLA2Id, fixture.CLA2Signature1Email, null);

        var data = result.Value;

        Assert.NotNull(data);
        Assert.Single(data);
        Assert.Equal(fixture.CLA2Signature1Email, data[0].Email);
        Assert.Null(data[0].GithubAccount);
        Assert.Equal(fixture.CLA2Signature1DeveloperUsername, data[0].DeveloperUsername);

        result = await controller.SearchSignatures(fixture.CLA2Id, fixture.CLA2Signature2Email, null);

        data = result.Value;

        Assert.NotNull(data);
        Assert.Single(data);
        Assert.Equal(fixture.CLA2Signature2Email, data[0].Email);
        Assert.Null(data[0].GithubAccount);
        Assert.Null(data[0].DeveloperUsername);
    }

    [Fact]
    public async Task CLASearch_ReturnsExactGithubResults()
    {
        var mailMock = new Mock<IMailQueue>();
        var storageMock = new Mock<ICLASignatureStorage>();
        var jobsMock = new Mock<IBackgroundJobClient>();

        var controller = new CLAController(logger, new ConfigurationBuilder().Build(),
            fixture.NotificationsEnabledDatabase, storageMock.Object, mailMock.Object, jobsMock.Object);

        var result = await controller.SearchSignatures(fixture.CLA2Id, null, fixture.CLA2Signature1Github);

        var data = result.Value;

        Assert.NotNull(data);
        Assert.Single(data);
        Assert.Equal(fixture.CLA2Signature1Github, data[0].GithubAccount);
        Assert.Null(data[0].Email);
        Assert.Equal(fixture.CLA2Signature1DeveloperUsername, data[0].DeveloperUsername);

        result = await controller.SearchSignatures(fixture.CLA2Id, null, fixture.CLA2Signature2Github);

        data = result.Value;

        Assert.NotNull(data);
        Assert.Single(data);
        Assert.Equal(fixture.CLA2Signature2Github, data[0].GithubAccount);
        Assert.Null(data[0].Email);
        Assert.Null(data[0].DeveloperUsername);
    }

    [Fact]
    public async Task CLASearch_ReturnsExactBothResults()
    {
        var mailMock = new Mock<IMailQueue>();
        var storageMock = new Mock<ICLASignatureStorage>();
        var jobsMock = new Mock<IBackgroundJobClient>();

        var controller = new CLAController(logger, new ConfigurationBuilder().Build(),
            fixture.NotificationsEnabledDatabase, storageMock.Object, mailMock.Object, jobsMock.Object);

        var result = await controller.SearchSignatures(fixture.CLA2Id, fixture.CLA2Signature1Email,
            fixture.CLA2Signature1Github);

        var data = result.Value;

        Assert.NotNull(data);
        Assert.Single(data);
        Assert.Equal(fixture.CLA2Signature1Email, data[0].Email);
        Assert.Equal(fixture.CLA2Signature1Github, data[0].GithubAccount);
        Assert.Equal(fixture.CLA2Signature1DeveloperUsername, data[0].DeveloperUsername);

        result = await controller.SearchSignatures(fixture.CLA2Id, fixture.CLA2Signature2Email,
            fixture.CLA2Signature2Github);

        data = result.Value;

        Assert.NotNull(data);
        Assert.Single(data);
        Assert.Equal(fixture.CLA2Signature2Email, data[0].Email);
        Assert.Equal(fixture.CLA2Signature2Github, data[0].GithubAccount);
        Assert.Null(data[0].DeveloperUsername);
    }

    [Fact]
    public async Task CLASearch_DoesNotQueryWrongCLA()
    {
        var mailMock = new Mock<IMailQueue>();
        var storageMock = new Mock<ICLASignatureStorage>();
        var jobsMock = new Mock<IBackgroundJobClient>();

        var controller = new CLAController(logger, new ConfigurationBuilder().Build(),
            fixture.NotificationsEnabledDatabase, storageMock.Object, mailMock.Object, jobsMock.Object);

        var result = await controller.SearchSignatures(fixture.CLA2Id, fixture.CLA1Signature1Email, null);

        var data = result.Value;

        Assert.NotNull(data);
        Assert.Empty(data);

        result = await controller.SearchSignatures(fixture.CLA1Id, fixture.CLA2Signature1Email, null);

        data = result.Value;

        Assert.NotNull(data);
        Assert.Empty(data);
    }

    [Fact]
    public async Task CLASearch_ReturnsExactPartialReturnsOtherResults()
    {
        var mailMock = new Mock<IMailQueue>();
        var storageMock = new Mock<ICLASignatureStorage>();
        var jobsMock = new Mock<IBackgroundJobClient>();

        var controller = new CLAController(logger, new ConfigurationBuilder().Build(),
            fixture.NotificationsEnabledDatabase, storageMock.Object, mailMock.Object, jobsMock.Object);

        var result = await controller.SearchSignatures(fixture.CLA2Id, fixture.CLA2Signature1Email,
            fixture.CLA2Signature1Github.TruncateWithoutEllipsis(AppInfo.PartialGithubMatchRevealAfterLenght));

        var data = result.Value;

        Assert.NotNull(data);
        Assert.Single(data);
        Assert.Equal(fixture.CLA2Signature1Email, data[0].Email);
        Assert.Equal(fixture.CLA2Signature1Github, data[0].GithubAccount);
        Assert.Equal(fixture.CLA2Signature1DeveloperUsername, data[0].DeveloperUsername);

        result = await controller.SearchSignatures(fixture.CLA2Id,
            fixture.CLA2Signature2Email.TruncateWithoutEllipsis(AppInfo.PartialEmailMatchRevealAfterLenght),
            fixture.CLA2Signature2Github);

        data = result.Value;

        Assert.NotNull(data);
        Assert.Single(data);
        Assert.Equal(fixture.CLA2Signature2Email, data[0].Email);
        Assert.Equal(fixture.CLA2Signature2Github, data[0].GithubAccount);
        Assert.Null(data[0].DeveloperUsername);
    }

    [Fact]
    public async Task CLASearch_NearMatchReturnsNothing()
    {
        var mailMock = new Mock<IMailQueue>();
        var storageMock = new Mock<ICLASignatureStorage>();
        var jobsMock = new Mock<IBackgroundJobClient>();

        var controller = new CLAController(logger, new ConfigurationBuilder().Build(),
            fixture.NotificationsEnabledDatabase, storageMock.Object, mailMock.Object, jobsMock.Object);

        var result = await controller.SearchSignatures(fixture.CLA2Id,
            fixture.CLA2Signature1Email.TruncateWithoutEllipsis(AppInfo.PartialEmailMatchRevealAfterLenght),
            fixture.CLA2Signature1Github.TruncateWithoutEllipsis(AppInfo.PartialGithubMatchRevealAfterLenght));

        var data = result.Value;

        Assert.NotNull(data);
        Assert.Empty(data);

        result = await controller.SearchSignatures(fixture.CLA2Id,
            fixture.CLA2Signature2Email.TruncateWithoutEllipsis(AppInfo.PartialEmailMatchRevealAfterLenght),
            fixture.CLA2Signature2Github.TruncateWithoutEllipsis(AppInfo.PartialGithubMatchRevealAfterLenght));

        data = result.Value;

        Assert.NotNull(data);
        Assert.Empty(data);
    }
}
