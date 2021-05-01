namespace ThriveDevCenter.Server.Tests.Fixtures
{
    using Microsoft.Extensions.Configuration;

    public sealed class RealUnitTestDatabaseFixture : RealTestDatabaseFixture
    {
        private static readonly object Lock = new object();
        private static bool databaseInitialized;

        public RealUnitTestDatabaseFixture() : base(GetConnectionString())
        {
            lock (Lock)
            {
                if (!databaseInitialized)
                {
                    Seed();
                    databaseInitialized = true;
                }
            }
        }

        private static string GetConnectionString()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<RealUnitTestDatabaseFixture>().Build();

            return configuration["UnitTestConnection"];
        }

        protected override void Seed()
        {
            RecreateDb();

            InsertBasicUsers();

            Database.SaveChanges();
        }
    }
}
