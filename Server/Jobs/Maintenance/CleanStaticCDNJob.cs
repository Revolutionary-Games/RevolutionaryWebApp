namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Utilities;

public class CleanStaticCDNJob : MaintenanceJobBase
{
    private static readonly string[] StaticAssets = ["page.js", "katex.min.js", "katex.min.css", "css/app.css"];

    private readonly IHttpClientFactory clientFactory;
    private readonly string? bunnyAPIKey;
    private readonly Uri? baseUrl;

    public CleanStaticCDNJob(ILogger<CleanStaticCDNJob> logger, IConfiguration configuration,
        ApplicationDbContext operationDb, NotificationsEnabledDb operationStatusDb,
        IHttpClientFactory clientFactory) : base(logger, operationDb, operationStatusDb)
    {
        this.clientFactory = clientFactory;
        bunnyAPIKey = configuration["CDN:BunnyCDN:APIKey"];
        baseUrl = configuration.GetLiveWWWBaseUrl();
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(bunnyAPIKey) || baseUrl == null)
        {
            operationData.Failed = true;
            operationData.ExtendedDescription = "No CDN API key found (or base URL missing)";
            return;
        }

        int succeeded = 0;
        int failed = 0;

        using var client = clientFactory.CreateClient("bunny");

        foreach (var asset in StaticAssets)
        {
            var finalUrl = new Uri(baseUrl, asset).ToString();

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "purge" + QueryString.Create("url", finalUrl));
                request.Headers.Add("AccessKey", bunnyAPIKey);

                var response = await client.SendAsync(request, cancellationToken);

                // Ensure the response status is a success
                response.EnsureSuccessStatusCode();

                logger.LogInformation("Successfully cleared CDN cache for : {FinalUrl}", finalUrl);
                ++succeeded;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to clear CDN for: {FinalUrl}", finalUrl);
                ++failed;
            }
        }

        if (failed > 0)
            operationData.Failed = true;

        operationData.ExtendedDescription = $"Cleared CDN cache of {succeeded} items and {failed} failed";
    }
}
