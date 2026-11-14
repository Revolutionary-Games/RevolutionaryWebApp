namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Server.Models;
using Server.Services;
using Shared.Models;
using Xunit;

public class RevolutionaryDiscordBotServiceTests
{
    private const string StatisticsApiUrl =
        "https://dev.revolutionarygamesstudio.com/api/v1/translations/projects/thrive/languages";

    private const string ProgressImageUrl =
        "https://dev.revolutionarygamesstudio.com/widgets/thrive/horizontal-auto.png";

    private const string TranslationProjectUrl = "https://dev.revolutionarygamesstudio.com/translations/thrive";

    [Fact]
    public async Task LoadsLanguageStatisticsFromConfiguredBackend()
    {
        var handler = new ResponseHandler(
            """[{"languageCode":"fi","displayName":"Finnish","total":100,"translated":20,"verified":30}]""");
        using var httpClient = new HttpClient(handler);
        await using var database = CreateDatabase();
        using var service = CreateService(httpClient, database);

        var languages = await service.LoadTranslationLanguageStatisticsAsync();

        Assert.Equal(StatisticsApiUrl, handler.RequestUrl);
        var language = Assert.Single(languages);
        Assert.Equal("fi", language.LanguageCode);
        Assert.Equal("Finnish", language.DisplayName);
        Assert.Equal(100, language.Total);
        Assert.Equal(20, language.Translated);
        Assert.Equal(30, language.Verified);
    }

    [Fact]
    public void EmbedIncludesAggregatedTranslatedAndVerifiedCountsAndProjectLink()
    {
        using var httpClient = new HttpClient();
        using var database = CreateDatabase();
        using var service = CreateService(httpClient, database);

        var embed = service.BuildTranslationStatisticsEmbed([
            new TranslationLanguageDTO { LanguageCode = "fi", Total = 100, Translated = 20, Verified = 30 },
            new TranslationLanguageDTO { LanguageCode = "de", Total = 100, Translated = 10, Verified = 10 },
        ]);

        Assert.Equal(TranslationProjectUrl, embed.Url);
        Assert.Equal("Thrive translation progress", embed.Title);
        Assert.Contains("70 of 200 translation entries complete (35%)", embed.Description);
        Assert.Contains("Across 2 languages.", embed.Description);
    }

    [Fact]
    public void EmbedHandlesZeroTotals()
    {
        using var httpClient = new HttpClient();
        using var database = CreateDatabase();
        using var service = CreateService(httpClient, database);

        var embed = service.BuildTranslationStatisticsEmbed([
            new TranslationLanguageDTO { LanguageCode = "fi" },
        ]);

        Assert.Contains("0 of 0 translation entries complete (0%)", embed.Description);
    }

    [Fact]
    public async Task HttpErrorsArePropagatedToOptionalStatisticsHandling()
    {
        using var httpClient = new HttpClient(new ResponseHandler(string.Empty, HttpStatusCode.ServiceUnavailable));
        await using var database = CreateDatabase();
        using var service = CreateService(httpClient, database);

        await Assert.ThrowsAsync<HttpRequestException>(service.LoadTranslationLanguageStatisticsAsync);
    }

    [Fact]
    public async Task DownloadsProgressPngBytesFromConfiguredBackend()
    {
        const string imageData = "progress-png-data";
        var handler = new ResponseHandler(imageData);
        using var httpClient = new HttpClient(handler);
        await using var database = CreateDatabase();
        using var service = CreateService(httpClient, database);

        var downloadedData = await service.DownloadTranslationProgressImageAsync();

        Assert.Equal(ProgressImageUrl, handler.RequestUrl);
        Assert.Equal(Encoding.UTF8.GetBytes(imageData), downloadedData);
    }

    [Fact]
    public async Task ProgressImageHttpErrorsArePropagatedToOptionalImageHandling()
    {
        using var httpClient = new HttpClient(new ResponseHandler(string.Empty, HttpStatusCode.ServiceUnavailable));
        await using var database = CreateDatabase();
        using var service = CreateService(httpClient, database);

        await Assert.ThrowsAsync<HttpRequestException>(service.DownloadTranslationProgressImageAsync);
    }

    [Fact]
    public void FallbackEmbedRetainsTranslationProjectLink()
    {
        using var httpClient = new HttpClient();
        using var database = CreateDatabase();
        using var service = CreateService(httpClient, database);

        var embed = service.BuildTranslationProjectEmbed();

        Assert.Equal(TranslationProjectUrl, embed.Url);
        Assert.Contains("View the Thrive translation project", embed.Description);
    }

    private static ApplicationDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ApplicationDbContext(options);
    }

    private static RevolutionaryDiscordBotService CreateService(HttpClient httpClient,
        ApplicationDbContext database)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseUrl"] = "https://dev.revolutionarygamesstudio.com/",
            ["Discord:RevolutionaryBot:Token"] = string.Empty,
            ["Discord:RevolutionaryBot:PreferDayProgressForRelease"] = "false",
            ["Discord:RevolutionaryBot:WikiBaseUrl"] = "https://wiki.revolutionarygamesstudio.com/wiki/",
            ["Discord:RevolutionaryBot:TranslationProgressUrl"] = ProgressImageUrl,
            ["Discord:RevolutionaryBot:TranslationStatisticsApiUrl"] = StatisticsApiUrl,
            ["Discord:RevolutionaryBot:TranslationProjectUrl"] = TranslationProjectUrl,
            ["Discord:RevolutionaryBot:WikiDefaultPreviewImage"] =
                "https://wiki.revolutionarygamesstudio.com/favicon.png",
            ["Discord:RevolutionaryBot:ProgressFont"] = "/fonts/Lato-Black.ttf",
            ["Discord:RevolutionaryBot:ProgressBackgroundImage"] = "/images/bot_progress_command_background.jpg",
        }).Build();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new RevolutionaryDiscordBotService(Substitute.For<ILogger<RevolutionaryDiscordBotService>>(),
            configuration, httpClientFactory, database);
    }

    private sealed class ResponseHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string? RequestUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            });
        }
    }
}
