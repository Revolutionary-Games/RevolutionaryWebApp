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

[DisableConcurrentExecution(500)]
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
            .ThenInclude(c => c.CombinedFromFeeds).AsSplitQuery()
            .Where(f => !f.Deleted && (f.ContentUpdatedAt == null || now - f.ContentUpdatedAt > f.PollInterval))
            .ToListAsync(cancellationToken);

        var client = httpClientFactory.CreateClient();

        var combinedToRefresh = new List<CombinedFeed>();

        // Refresh all the feeds
        foreach (var feed in feedsToRefresh)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            int previousContent;
            List<ParsedFeedItem> items;
            try
            {
                var response = await client.GetAsync(feed.Url, cancellationToken);
                response.EnsureSuccessStatusCode();

                previousContent = feed.LatestContentHash;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                items = feed.ProcessContent(content).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to fetch or process feed {Name} ({Id})", feed.Name, feed.Id);
                continue;
            }

            if (previousContent == feed.LatestContentHash)
            {
                // Feed data has not changed
                continue;
            }

            logger.LogInformation("New content for feed {Name}", feed.Name);

            if (cancellationToken.IsCancellationRequested)
                break;

            foreach (var combinedFeed in feed.CombinedInto)
            {
                if (!combinedToRefresh.Contains(combinedFeed))
                    combinedToRefresh.Add(combinedFeed);
            }

            // Filter out items that have already been processed
            var ids = items.Select(i => i.Id).ToList();
            var alreadyProcessedItems = await database.SeenFeedItems.Where(i => ids.Contains(i.ItemIdentifier))
                .Select(i => i.ItemIdentifier).ToListAsync(cancellationToken);
            items = items.Where(i => !alreadyProcessedItems.Contains(i.Id)).ToList();

            if (items.Count < 1)
                continue;

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

            // Save to database the items we have processed now. We don't want to cancel here as the webhooks are
            // already on their way.
            // ReSharper disable once MethodSupportsCancellation
            await database.SeenFeedItems.AddRangeAsync(items.Select(i => new SeenFeedItem(feed.Id, i.Id)));
        }

        logger.LogInformation("Refreshed {Count} feeds", feedsToRefresh.Count);

        // Refresh combined feeds that didn't have anything yet or are much older than what they consist of
        if (!cancellationToken.IsCancellationRequested)
        {
            var outdatedThreshold = TimeSpan.FromHours(1);

            var existingIds = combinedToRefresh.Select(c => c.Id).ToHashSet();

            List<CombinedFeed> needingRefresh;

            try
            {
                needingRefresh = await database.CombinedFeeds.Where(c => !existingIds.Contains(c.Id))
                    .Include(c => c.CombinedFromFeeds).Where(c =>
                        c.ContentUpdatedAt == null || c.CombinedFromFeeds.Any(f =>
                            f.ContentUpdatedAt != null && f.ContentUpdatedAt - c.ContentUpdatedAt > outdatedThreshold))
                    .ToListAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Getting outdated combined feeds, canceled");
                needingRefresh = new List<CombinedFeed>();
            }

            foreach (var combinedFeed in needingRefresh)
            {
                logger.LogInformation(
                    "Combined feed {Name} has no content or is much more outdated than the feeds " +
                    "it consists of, updating",
                    combinedFeed.Name);
            }

            combinedToRefresh.AddRange(needingRefresh);
        }

        // Don't run the db save or try to process if there's nothing to do
        if (feedsToRefresh.Count < 1 && combinedToRefresh.Count < 1)
            return;

        // Update combined feeds that need to be updated
        if (!cancellationToken.IsCancellationRequested)
        {
            foreach (var combinedFeed in combinedToRefresh)
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
