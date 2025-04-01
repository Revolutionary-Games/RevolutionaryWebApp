namespace RevolutionaryWebApp.Server.Jobs.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models.Pages;
using Utilities;

/// <summary>
///   Used to trigger some extra cache clearing options when a new page is published (latest posts page etc.)
/// </summary>
public class OnNewPagePublishedJob
{
    private readonly ILogger<OnNewPagePublishedJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IHttpClientFactory clientFactory;
    private readonly string? bunnyAPIKey;
    private readonly Uri? baseUrl;

    public OnNewPagePublishedJob(ILogger<OnNewPagePublishedJob> logger, IConfiguration configuration,
        ApplicationDbContext database, IHttpClientFactory clientFactory)
    {
        this.logger = logger;
        this.database = database;
        this.clientFactory = clientFactory;
        bunnyAPIKey = configuration["CDN:BunnyCDN:APIKey"];
        baseUrl = configuration.GetLiveWWWBaseUrl();
    }

    public async Task Execute(long pageId, CancellationToken cancellationToken)
    {
        var page = await database.VersionedPages.AsNoTracking().Where(p => p.Id == pageId)
            .FirstOrDefaultAsync(cancellationToken);

        if (page == null)
        {
            logger.LogWarning("Failed to get page ({PageId}) for new page detection, assuming it is deleted", pageId);
            return;
        }

        if (page.Type == PageType.Post)
        {
            // TODO: clearing from LiveController.CacheKeyForNewsFeedPage(0) though that uses memory cache so clearing
            // that won't work in a multi server setup

            // Clear the latest news page to make the new page appear sooner
            if (string.IsNullOrEmpty(bunnyAPIKey) || baseUrl == null)
            {
                logger.LogWarning("No CDN API key found (or base URL missing), skipping CDN cache clear for news page");
                return;
            }

            var cdnUrlsToClear = new List<string> { "news", "news/page/0", "news/page/0/" };

            // Should clear cache for the previously latest published page as that will link to the new page now
            var previous = await database.VersionedPages.AsNoTracking()
                .Where(p => p.Type == PageType.Post && p.PublishedAt != null && p.PublishedAt < page.PublishedAt &&
                    p.Visibility == PageVisibility.Public && !p.Deleted && p.Id != page.Id)
                .OrderByDescending(p => p.PublishedAt).FirstOrDefaultAsync(cancellationToken);

            if (previous is { Permalink: not null })
            {
                logger.LogInformation("Clearing cache for previously latest published page");
                cdnUrlsToClear.Add(previous.Permalink);
                cdnUrlsToClear.Add(previous.Permalink + "/");
            }

            using var client = clientFactory.CreateClient("bunny");

            foreach (var permalink in cdnUrlsToClear)
            {
                var finalUrl = new Uri(baseUrl, permalink).ToString();

                try
                {
                    var request =
                        new HttpRequestMessage(HttpMethod.Post, "purge" + QueryString.Create("url", finalUrl));
                    request.Headers.Add("AccessKey", bunnyAPIKey);

                    var response = await client.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    logger.LogInformation("Successfully cleared CDN cache for: {FinalUrl}", finalUrl);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to clear CDN cache for URL: {FinalUrl}", finalUrl);
                }
            }
        }
    }
}
