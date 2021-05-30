namespace ThriveDevCenter.Server.Tests.Fixtures
{
    public class EditableInMemoryDatabaseFixture : BaseSharedDatabaseFixture
    {
        public EditableInMemoryDatabaseFixture(string uniqueName) : base(uniqueName)
        {
        }

        protected override void Seed()
        {
        }
    }
}
