namespace ThriveDevCenter.Client.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Converters;
    using Utilities;

    public class UsernameRetriever
    {
        // Seems like this may be useful in the future at some point
        // ReSharper disable once NotAccessedField.Local
        private readonly CurrentUserInfo userInfo;
        private readonly HttpClient http;

        private readonly Dictionary<long, string> usernameCache = new();
        private readonly SemaphoreSlim usernameLock = new(1);

        private readonly List<long> queuedUsernamesToFetch = new();
        private readonly SemaphoreSlim fetchQueueLock = new(1);
        private readonly SemaphoreSlim fetchLock = new(1);

        public UsernameRetriever(CurrentUserInfo userInfo, HttpClient http)
        {
            this.userInfo = userInfo;
            this.http = http;
        }

        public async ValueTask<string> GetUsername(long id)
        {
            bool query = false;
            
            while (true)
            {
                await usernameLock.WaitAsync();
                try
                {
                    if (usernameCache.TryGetValue(id, out var username))
                    {
                        return username;
                    }
                }
                finally
                {
                    usernameLock.Release();
                }

                // We only add the ID to query *once* to the list of things to query
                if(!query)
                {
                    await fetchQueueLock.WaitAsync();
                    try
                    {
                        if (!queuedUsernamesToFetch.Contains(id))
                        {
                            queuedUsernamesToFetch.Add(id);
                            query = true;
                        }
                    }
                    finally
                    {
                        fetchQueueLock.Release();
                    }
                }

                await Task.Delay(AppInfo.WaitBeforeNameRetrieveBatchStart);
                await PerformQueries();
            }
        }

        private async Task PerformQueries()
        {
            await fetchLock.WaitAsync();
            try
            {
                List<long> batch;

                await fetchQueueLock.WaitAsync();
                try
                {
                    if (queuedUsernamesToFetch.Count < 1)
                    {
                        // Nothing to fetch, exit this method and hope that the cache has the value the loop this is
                        // called in is looking for
                        return;
                    }

                    batch = queuedUsernamesToFetch.Take(AppInfo.UsernameRetrieveBatchSize).ToList();
                    queuedUsernamesToFetch.RemoveRange(0,
                        Math.Min(queuedUsernamesToFetch.Count, AppInfo.UsernameRetrieveBatchSize));
                }
                finally
                {
                    fetchQueueLock.Release();
                }

                await QueryBatch(batch);
            }
            finally
            {
                fetchLock.Release();
            }
        }

        private async Task QueryBatch(List<long> batch)
        {
            Dictionary<long, string> result;

            try
            {
                var response = await http.PostAsJsonAsync("api/v1/UserManagement/usernames", batch);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();

                result = JsonSerializer.Deserialize<Dictionary<long, string>>(content,
                    HttpClientHelpers.GetOptionsWithSerializers()) ?? throw new NullDecodedJsonException();
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Failed to retrieve usernames, exception: {e}");

                // Write errors for all the IDs that we tried to retrieve
                // TODO: should we instead just sleep here and retry later?
                await usernameLock.WaitAsync();
                try
                {
                    foreach (var failedId in batch)
                    {
                        usernameCache[failedId] = $"Failed to retrieve user with id {failedId}";
                    }
                }
                finally
                {
                    usernameLock.Release();
                }

                return;
            }

            // Successfully fetched, store in the cache where the code looking for the results will find them
            await usernameLock.WaitAsync();
            try
            {
                foreach (var (key, value) in result)
                {
                    usernameCache[key] = value;
                }
            }
            finally
            {
                usernameLock.Release();
            }
        }
    }
}
