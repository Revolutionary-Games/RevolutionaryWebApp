namespace RevolutionaryWebApp.Server.Services;

using System.Net;
using System.Threading.Tasks;
using Models;

public interface IDownloadQuota
{
    public const string LimitReachedMessage = "Weekly download quota reached. Wait for your quota to reset.";

    public const string PublicLfsLimitReachedMessage =
        "Login required: the public IP download quota was reached. Log in or wait a week to download more.";

    public long GetLimit(User? user);

    public Task<long?> GetUsageAsync(long userId);

    public Task<bool> TryReserveAsync(User? user, IPAddress? address, long bytes);
}
