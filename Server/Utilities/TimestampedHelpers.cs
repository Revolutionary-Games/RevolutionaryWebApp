namespace RevolutionaryWebApp.Server.Utilities;

using System;
using Models;

public static class TimestampedHelpers
{
    public static void BumpUpdatedAt(this IHasUpdatedAt entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
    }
}
