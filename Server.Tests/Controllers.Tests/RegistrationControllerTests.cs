namespace ThriveDevCenter.Server.Tests.Controllers.Tests
{
    using Dummies;
    using Server.Controllers;
    using Utilities;
    using Xunit;
    using Xunit.Abstractions;

    public class RegistrationControllerTests
    {
        private readonly XunitLogger<RegistrationController> logger;

        public RegistrationControllerTests(ITestOutputHelper output)
        {
            logger = new XunitLogger<RegistrationController>(output);
        }

        [Fact]
        public void Get_ReturnsRegistrationEnabledStatus()
        {
            var controller = new RegistrationController(logger, null, new DummyRegistrationStatus()
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
            var controller = new RegistrationController(logger, null, new DummyRegistrationStatus()
            {
                RegistrationEnabled = false
            }, null, null);

            var result = controller.Get();

            Assert.False(result);
        }
    }
}
