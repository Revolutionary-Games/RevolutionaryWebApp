namespace ThriveDevCenter.Server.Tests.Fixtures;

using Server.Services;

public class EditableInMemoryDatabaseFixture : BaseSharedDatabaseFixture
{
    public EditableInMemoryDatabaseFixture(string uniqueName) : base(uniqueName)
    {
    }

    protected override void Seed()
    {
    }
}

public class EditableInMemoryDatabaseFixtureWithNotifications : BaseSharedDatabaseFixtureWithNotifications
{
    public EditableInMemoryDatabaseFixtureWithNotifications(IModelUpdateNotificationSender notificationSender,
        string uniqueName) : base(notificationSender, uniqueName)
    {
    }

    protected override void Seed()
    {
    }
}
