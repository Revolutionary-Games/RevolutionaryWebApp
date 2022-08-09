namespace ThriveDevCenter.Server.Tests.Models.Tests
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Fixtures;
    using Server.Models;
    using Xunit;

    public class DevBuildTests : IClassFixture<EmptyDatabaseFixture>
    {
        private readonly EmptyDatabaseFixture fixture;

        public DevBuildTests(EmptyDatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public void DevBuildVerification_FailsToSetBOTDWithoutDescription(string description)
        {
            var build = new DevBuild()
            {
                BuildHash = "123445",
                BuildZipHash = "aabababa",
                Branch = "master",
                Platform = "Linux/X11",
                BuildOfTheDay = true,
                Description = description,
            };

            List<ValidationResult> result = new List<ValidationResult>();
            var valid = Validator.TryValidateObject(build, new ValidationContext(build), result);

            Assert.False(valid);
            Assert.NotEmpty(result);
        }

        [Fact]
        public async Task DevBuildVerification_CanValidateAndSaveCorrectBOTD()
        {
            var build = new DevBuild()
            {
                BuildHash = "123445",
                BuildZipHash = "aabababa",
                Branch = "master",
                Platform = "Linux/X11",
                BuildOfTheDay = true,
                Description = "A cool description",
            };

            List<ValidationResult> result = new List<ValidationResult>();
            var valid = Validator.TryValidateObject(build, new ValidationContext(build), result);

            Assert.True(valid);
            Assert.Empty(result);

            await fixture.Database.DevBuilds.AddAsync(build);

            await fixture.Database.SaveChangesAsync();
        }
    }
}
