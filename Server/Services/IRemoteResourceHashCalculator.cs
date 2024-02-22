namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

public interface IRemoteResourceHashCalculator
{
    public Task<string> Sha256(string url, CancellationToken cancellationToken);
}

public class RemoteResourceHashCalculator : IRemoteResourceHashCalculator
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly TimeSpan refreshInterval = TimeSpan.FromHours(8);

    private readonly Dictionary<string, (string Hash, DateTime Created)> sha256Hashes = new();

    public RemoteResourceHashCalculator(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public async Task<string> Sha256(string url, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        lock (sha256Hashes)
        {
            if (sha256Hashes.TryGetValue(url, out var data))
            {
                if (now - data.Created < refreshInterval)
                    return data.Hash;
            }
        }

        var newValue = await ComputeSha256Of(new Uri(url), cancellationToken);
        lock (sha256Hashes)
        {
            sha256Hashes[url] = (newValue, now);
        }

        return newValue;
    }

    private async Task<string> ComputeSha256Of(Uri url, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        return Convert.ToHexString(await SHA256.Create()
                .ComputeHashAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken))
            .ToLowerInvariant();
    }
}
