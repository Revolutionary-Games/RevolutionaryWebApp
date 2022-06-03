namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

public class RefreshFeedsJob : IJob
{
    private readonly ILogger<RefreshFeedsJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IBackgroundJobClient jobClient;

    public RefreshFeedsJob(ILogger<RefreshFeedsJob> logger, NotificationsEnabledDb database,
        IHttpClientFactory httpClientFactory, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.httpClientFactory = httpClientFactory;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        // Detect feeds needing refreshing
        var now = DateTime.UtcNow;
        var feedsToRefresh = await database.Feeds.Include(f => f.DiscordWebhooks).Include(f => f.CombinedInto)
            .ThenInclude(c => c.CombinedFromFeeds)
            .Where(f => !f.Deleted && (f.ContentUpdatedAt == null || now - f.ContentUpdatedAt > f.PollInterval))
            .ToListAsync(cancellationToken);

        if (feedsToRefresh.Count < 1)
            return;

        var client = httpClientFactory.CreateClient();

        var combinedToRefresh = new List<CombinedFeed>();

        // Refresh all the feeds
        foreach (var feed in feedsToRefresh)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var response = await client.GetAsync(feed.Url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var previousContent = feed.ContentUpdatedAt;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var items = feed.ProcessContent(content).ToList();

            if (previousContent == feed.ContentUpdatedAt)
            {
                // Feed data has not changed
                continue;
            }

            logger.LogInformation("New content for feed {Name}", feed.Name);

            if (cancellationToken.IsCancellationRequested)
                break;

            foreach (var combinedFeed in feed.CombinedInto)
            {
                combinedToRefresh.Add(combinedFeed);
            }

            // Fire webhooks
            foreach (var webhook in feed.DiscordWebhooks)
            {
                foreach (var feedItem in items)
                {
                    var message = webhook.GetMessage(feedItem);
                    jobClient.Enqueue<SendDiscordManualWebhookJob>(x =>
                        x.Execute(webhook.WebhookUrl, message, CancellationToken.None));
                }
            }
        }

        // Detect combined feeds that need to be updated
        if (!cancellationToken.IsCancellationRequested)
        {
            foreach (var combinedFeed in combinedToRefresh.DistinctBy(c => c.Id))
            {
                logger.LogInformation("Updating combined feed {Name}", combinedFeed.Name);

                // TODO: check that the combined from objects here are the same ones as that get updated above
                combinedFeed.ProcessContent(combinedFeed.CombinedFromFeeds);

                if (cancellationToken.IsCancellationRequested)
                    break;
            }
        }

        // Don't want to skip saving here once we have already sent messages about created items
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();
    }
}
