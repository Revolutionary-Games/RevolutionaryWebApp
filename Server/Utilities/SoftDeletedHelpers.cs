namespace RevolutionaryWebApp.Server.Utilities;

using DevCenterCommunication.Models;
using Shared.Models;

public static class SoftDeletedHelpers
{
    public static SoftDeletedResource FromModel(ITimestampedModel model, string name)
    {
        return new()
        {
            Id = model.Id,
            Name = name,
            UpdatedAt = model.UpdatedAt,
        };
    }
}
