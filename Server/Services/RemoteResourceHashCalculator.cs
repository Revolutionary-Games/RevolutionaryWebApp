namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;

    public class RemoteResourceHashCalculator : IRemoteResourceHashCalculator
    {
        private readonly IHttpClientFactory httpClientFactory;
        private readonly TimeSpan refreshInterval = TimeSpan.FromHours(8);

        private readonly Dictionary<string, (string hash, DateTime created)> sha256Hashes = new();

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
                    if (now - data.created < refreshInterval)
                        return data.hash;
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
            var response = await client.GetAsync(url, cancellationToken);

            return Convert.ToHexString(await SHA256.Create()
                    .ComputeHashAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken))
                .ToLowerInvariant();
        }
    }

    public interface IRemoteResourceHashCalculator
    {
        Task<string> Sha256(string url, CancellationToken cancellationToken);
    }
}
