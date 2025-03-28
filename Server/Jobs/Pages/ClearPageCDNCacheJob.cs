namespace RevolutionaryWebApp.Server.Jobs.Pages;

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Utilities;

/// <summary>
///   Clears the CDN cache for a page (if we have an API key)
/// </summary>
public class ClearPageCDNCacheJob
{
    private readonly ILogger<ClearPageCDNCacheJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IHttpClientFactory clientFactory;
    private readonly string? bunnyAPIKey;
    private readonly Uri? baseUrl;

    public ClearPageCDNCacheJob(ILogger<ClearPageCDNCacheJob> logger, IConfiguration configuration,
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
        if (string.IsNullOrEmpty(bunnyAPIKey) || baseUrl == null)
        {
            logger.LogWarning("No CDN API key found (or base URL missing), skipping CDN cache clear for page");
            return;
        }

        var page = await database.VersionedPages.AsNoTracking().Where(p => p.Id == pageId)
            .FirstOrDefaultAsync(cancellationToken);

        if (page == null)
        {
            logger.LogWarning("Failed to get page ({PageId}) for CDN cache clear, assuming it is deleted", pageId);
            return;
        }

        if (string.IsNullOrWhiteSpace(page.Permalink))
        {
            logger.LogInformation("Page ({PageId}) has no permalink, skipping CDN cache clear", pageId);
            return;
        }

        var finalUrl = new Uri(baseUrl, page.Permalink).ToString();

        using var client = clientFactory.CreateClient("bunny");

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "purge" + QueryString.Create("url", finalUrl));

            request.Headers.Add("AccessKey", bunnyAPIKey);

            var response = await client.SendAsync(request, cancellationToken);

            // Ensure the response status is a success
            response.EnsureSuccessStatusCode();

            // Clear also the URL with a trailing '/' as that is also a valid way to access the page
            request.RequestUri = new Uri(finalUrl + "/");

            if (!request.RequestUri.ToString().EndsWith("/"))
                throw new Exception("Failed to append trailing slash to URL");

            response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            logger.LogInformation("Successfully cleared CDN cache for page ({PageId}) at URL: {FinalUrl}", pageId,
                finalUrl);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to clear CDN cache for page ({PageId}) at URL: {FinalUrl}", pageId, finalUrl);
        }
    }
}
