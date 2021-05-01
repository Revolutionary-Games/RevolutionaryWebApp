namespace ThriveDevCenter.Server.Tests.Controllers.Tests
{
    using System.Threading.Tasks;
    using Dummies;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Moq;
    using Server.Authorization;
    using Server.Controllers;
    using Server.Models;
    using Server.Services;
    using Shared.Forms;
    using Utilities;
    using Xunit;
    using Xunit.Abstractions;

    public class RegistrationControllerTests
    {
        private const string RegistrationCode = "Code";
        private readonly XunitLogger<RegistrationController> logger;

        private readonly DbContextOptions<ApplicationDbContext> dbOptions =
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("RegistrationTestDB").Options;

        private readonly DummyRegistrationStatus dummyRegistrationStatus = new DummyRegistrationStatus()
        {
            RegistrationEnabled = true,
            RegistrationCode = RegistrationCode
        };

        public RegistrationControllerTests(ITestOutputHelper output)
        {
            logger = new XunitLogger<RegistrationController>(output);
        }

        [Fact]
        public void Get_ReturnsRegistrationEnabledStatus()
        {
            var controller = new RegistrationController(logger, new DummyRegistrationStatus()
            {
                RegistrationEnabled = true,
                RegistrationCode = "abc123"
            }, null, null);

            var result = controller.Get();

            Assert.True(result);
        }

        [Fact]
        public void Get_ReturnsRegistrationDisabledStatus()
        {
            var controller = new RegistrationController(logger, new DummyRegistrationStatus()
            {
                RegistrationEnabled = false
            }, null, null);

            var result = controller.Get();

            Assert.False(result);
        }

        [Theory]
        [InlineData("1234")]
        [InlineData(null)]
        public async Task Registration_FailsOnInvalidCSRF(string csrfValue)
        {
            var csrfMock = new Mock<ITokenVerifier>();
            csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, null, false))
                .Returns(false).Verifiable();

            var notificationsMock = new Mock<IModelUpdateNotificationSender>();

            await using var database = new NotificationsEnabledDb(dbOptions, notificationsMock.Object);

            var controller = new RegistrationController(logger, dummyRegistrationStatus, csrfMock.Object, database);

            var result = await controller.Post(new RegistrationFormData()
            {
                CSRF = csrfValue, Email = "test@example.com", Name = "test", Password = "password12345",
                RegistrationCode = RegistrationCode
            });

            var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);

            Assert.Equal(400, objectResult.StatusCode);
            Assert.Empty(database.Users);

            csrfMock.Verify();
        }

        [Fact]
        public async Task Registration_FailsOnInvalidCode()
        {
            var csrfMock = new Mock<ITokenVerifier>();
            csrfMock.Setup(csrf => csrf.IsValidCSRFToken(It.IsNotNull<string>(), null, false))
                .Returns(true).Verifiable();

            var notificationsMock = new Mock<IModelUpdateNotificationSender>();

            await using var database = new NotificationsEnabledDb(dbOptions, notificationsMock.Object);

            var controller = new RegistrationController(logger, dummyRegistrationStatus,
                csrfMock.Object, database);

            var result = await controller.Post(new RegistrationFormData()
            {
                CSRF = "aValue", Email = "test@example.com", Name = "test", Password = "password12345",
                RegistrationCode = RegistrationCode + "a"
            });

            var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);

            Assert.Equal(400, objectResult.StatusCode);
            Assert.Empty(database.Users);

            csrfMock.Verify();
        }

        [Fact]
        public async Task Registration_SucceedsAndCreatesUser()
        {
            var csrfValue = "JustSomeRandomString";

            var csrfMock = new Mock<ITokenVerifier>();
            csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, null, false))
                .Returns(true).Verifiable();
            var notificationsMock = new Mock<IModelUpdateNotificationSender>();

            notificationsMock
                .Setup(notifications => notifications.OnChangesDetected(EntityState.Added,
                    It.IsAny<User>(), false))
                .Returns(Task.CompletedTask).Verifiable();

            await using var database = new NotificationsEnabledDb(dbOptions, notificationsMock.Object);

            var controller = new RegistrationController(logger, dummyRegistrationStatus, csrfMock.Object, database);

            var result = await controller.Post(new RegistrationFormData()
            {
                CSRF = csrfValue, Email = "test@example.com", Name = "test", Password = "password12345",
                RegistrationCode = RegistrationCode
            });

            var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);

            Assert.Equal(201, objectResult.StatusCode);

            Assert.NotEmpty(database.Users);
            var user = await database.Users.FirstAsync();

            Assert.Equal("test@example.com", user.Email);
            Assert.Equal("test", user.UserName);
            Assert.NotEqual("password12345", user.PasswordHash);
            Assert.True(Passwords.CheckPassword(user.PasswordHash, "password12345"));

            notificationsMock.Verify();
        }
    }
}
