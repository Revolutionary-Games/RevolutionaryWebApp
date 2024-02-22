namespace RevolutionaryWebApp.Shared.Models;

public class StorageUsageStats
{
    public StorageUsageStats(long usedBytes)
    {
        UsedBytes = usedBytes;
    }

    public long UsedBytes { get; }
}
