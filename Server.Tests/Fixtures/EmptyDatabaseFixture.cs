namespace ThriveDevCenter.Server.Tests.Fixtures;

public class EmptyDatabaseFixture : BaseSharedDatabaseFixture
{
    public EmptyDatabaseFixture() : base("EmptyTestDatabase")
    {
    }

    protected sealed override void Seed()
    {
    }
}
